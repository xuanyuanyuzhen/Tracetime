using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using TraceTime.Core;

namespace TraceTime.App.Windows;

internal sealed class ForegroundReader
{
    private readonly Dictionary<string, AppIdentity> _apps = new(StringComparer.OrdinalIgnoreCase);
    public TrackingSample Read()
    {
        var mono = (long)(Stopwatch.GetTimestamp() * (1000d / Stopwatch.Frequency));
        var info = new LastInput { Size = (uint)Marshal.SizeOf<LastInput>() };
        long idle = 0;
        if (GetLastInputInfo(ref info))
        {
            idle = unchecked((uint)Environment.TickCount64 - info.Time);
            if (idle > TimeSpan.FromDays(7).TotalMilliseconds) idle = 0;
        }
        return new(DateTimeOffset.UtcNow, mono, idle, ReadApp());
    }

    public AppIdentity ReadApp()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return AppIdentity.Unknown;
        var className = new StringBuilder(256); GetClassName(hwnd, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return AppIdentity.System;
        GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == 0) return AppIdentity.Unknown;
        var path = ProcessPath(pid);
        if (path is not null && Path.GetFileNameWithoutExtension(path).Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase))
        {
            uint childPid = 0;
            EnumChildWindows(hwnd, (child, _) => {
                GetWindowThreadProcessId(child, out var p);
                if (p != pid && p != 0 && IsWindowVisible(child)) { childPid = p; return false; }
                return true;
            }, IntPtr.Zero);
            if (childPid != 0) { pid = childPid; path = ProcessPath(pid); }
        }
        try
        {
            string processName;
            if(path is not null) processName=Path.GetFileNameWithoutExtension(path);
            else { using var process=Process.GetProcessById((int)pid); processName=process.ProcessName; }
            if (processName is "StartMenuExperienceHost" or "SearchHost" or "ShellExperienceHost" or "LockApp") return AppIdentity.System;
            var key = (path ?? "process:" + processName).ToLowerInvariant();
            if (_apps.TryGetValue(key, out var cached)) return cached;
            var name = path is null ? processName : FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (string.IsNullOrWhiteSpace(name)) name = processName;
            if (name.Length > 80) name = name[..80];
            return _apps[key] = new(key, name, path);
        }
        catch { return AppIdentity.Unknown; }
    }

    private static string? ProcessPath(uint pid)
    {
        var handle = OpenProcess(0x1000, false, pid);
        if (handle == IntPtr.Zero) return null;
        try { var text = new StringBuilder(32768); var size = text.Capacity; return QueryFullProcessImageName(handle, 0, text, ref size) ? text.ToString() : null; }
        finally { CloseHandle(handle); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct LastInput { public uint Size; public uint Time; }
    private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr param);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
