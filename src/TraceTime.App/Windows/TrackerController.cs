using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using Microsoft.Win32;
using TraceTime.Core;
using TraceTime.Storage;

namespace TraceTime.App.Windows;

internal sealed class TrackerController : IDisposable
{
    private readonly UsageDatabase _db;
    private readonly TrackingEngine _engine;
    private readonly ForegroundReader _reader = new();
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private readonly WinEventProc _callback;
    private IntPtr _hook;
    private bool _locked, _sleeping, _disposed;
    private string _mode = "";
    private GapInterval? _gap;
    private int _ticks;
    public bool Paused { get; private set; }
    public bool IsIdle => _engine.IsIdle;
    public string? Error { get; private set; }
    public AppIdentity CurrentApp { get; private set; } = AppIdentity.Unknown;
    public AppSettings Settings { get; private set; }
    public event Action? Changed;
    public string Status => Error is not null ? "保存失败，正在重试" : _mode.Length > 0 ? _mode : IsIdle ? "记录中 · 空闲" : "正在记录";

    public TrackerController(UsageDatabase db, Dispatcher dispatcher)
    {
        _db = db; _dispatcher = dispatcher; Settings = db.LoadSettings();
        _engine = new(TimeSpan.FromMinutes(Settings.IdleMinutes));
        _locked = IsSessionLocked();
        _callback = (_, _, _, _, _, _, _) => {
            if (!_disposed && !_dispatcher.HasShutdownStarted) _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Tick));
        };
        _hook = SetWinEventHook(3, 3, IntPtr.Zero, _callback, 0, 0, 0);
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Tick(), dispatcher);
        ApplyMode(); Tick();
    }

    private void Observe()
    {
        var sample = _reader.Read(); CurrentApp = sample.App; _engine.Observe(sample);
    }
    private void Tick()
    {
        if (_disposed) return;
        if (_mode.Length == 0) Observe();
        if (++_ticks % 5 == 0) Flush();
        Changed?.Invoke();
    }

    public void Flush()
    {
        try
        {
            _engine.Flush(_db);
            if (_gap is not null) _db.SaveGap(_gap with { End = DateTimeOffset.UtcNow });
            Error = null;
        }
        catch (Exception ex) { Error = "无法保存记录：" + ex.Message; }
    }

    public void TogglePause() { Paused = !Paused; ApplyMode(); }
    private void ApplyMode()
    {
        var next = _sleeping ? "休眠" : _locked ? "锁屏 / 会话断开" : Paused ? "已暂停" : "";
        if (next == _mode) return;
        if (_mode.Length == 0) { Observe(); _engine.Stop(); }
        Flush();
        _gap = null; _mode = next;
        if (next.Length > 0) _gap = new(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, next);
        else Observe();
        Changed?.Invoke();
    }

    public void UpdateSettings(AppSettings settings)
    {
        if (_mode.Length == 0) Observe();
        Flush();
        if (Error is not null) throw new InvalidOperationException(Error);
        _db.SaveSettings(settings); Settings = settings;
        _engine.ChangeThreshold(TimeSpan.FromMinutes(settings.IdleMinutes));
        if (_mode.Length == 0) Observe();
        Changed?.Invoke();
    }

    public void DeleteRange(DateTimeOffset start, DateTimeOffset end)
    {
        if (_mode.Length == 0) Observe();
        _engine.Stop(); Flush();
        if (Error is not null) throw new InvalidOperationException(Error);
        try { _db.DeleteRange(start, end); }
        finally
        {
            if (_gap is not null) _gap = _gap with { Id = Guid.NewGuid().ToString("N"), Start = DateTimeOffset.UtcNow, End = DateTimeOffset.UtcNow };
            if (_mode.Length == 0) Observe();
        }
        Changed?.Invoke();
    }

    private void OnDispatcher(Action action)
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.Invoke(() => { if (!_disposed) { action(); ApplyMode(); } });
    }

    private void SessionChanged(object sender, SessionSwitchEventArgs e) => OnDispatcher(() => {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect) _locked = true;
        if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect) _locked = false;
    });
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) => OnDispatcher(() => {
        if (e.Mode == PowerModes.Suspend) _sleeping = true;
        if (e.Mode == PowerModes.Resume) _sleeping = false;
    });

    private static bool IsSessionLocked()
    {
        // WTSINFOEX level 1: Level at offset 0, union aligned at 8, SessionFlags at 16 (Windows 10+).
        if (!WTSQuerySessionInformation(IntPtr.Zero, -1, 25, out var buffer, out var bytes)) return false;
        try { return bytes >= 20 && Marshal.ReadInt32(buffer) == 1 && Marshal.ReadInt32(buffer, 16) == 0; }
        finally { WTSFreeMemory(buffer); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (_mode.Length == 0) Observe();
        _engine.Stop(); Flush(); _disposed = true; _timer.Stop();
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        SystemEvents.SessionSwitch -= SessionChanged; SystemEvents.PowerModeChanged -= PowerChanged;
        GC.KeepAlive(_callback);
    }
    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("wtsapi32.dll", CharSet=CharSet.Unicode)] private static extern bool WTSQuerySessionInformation(IntPtr server, int session, int infoClass, out IntPtr buffer, out int bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(IntPtr buffer);
}
