using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using TraceTime.App.Windows;
using TraceTime.Storage;

namespace TraceTime.App;

public partial class App : Application
{
    private Mutex? _mutex;
    private EventWaitHandle? _activation;
    private RegisteredWaitHandle? _activationWait;
    private UsageDatabase? _db;
    private TrackerController? _tracker;
    internal bool Exiting { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            string? Option(string key) { var i = Array.IndexOf(e.Args,key); return i >= 0 && i+1<e.Args.Length ? e.Args[i+1] : null; }
            var demo = e.Args.Contains("--demo");
            var dataDir = Option("--data-dir") ?? (demo ? Path.Combine(Path.GetTempPath(),"TraceTime-demo-"+Guid.NewGuid().ToString("N")) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TraceTime"));
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(dataDir).ToLowerInvariant())))[..20];
            _mutex = new Mutex(true,@"Local\TraceTime-"+hash,out var created);
            _activation = new EventWaitHandle(false,EventResetMode.AutoReset,@"Local\TraceTime-open-"+hash);
            if(!created) { _activation.Set(); Shutdown(); return; }
            _db = new UsageDatabase(Path.Combine(dataDir,"usage.db"));
            if(demo) DemoData.Seed(_db);
            else _tracker = new TrackerController(_db,Dispatcher);
            var window = new MainWindow(_db,_tracker,demo);
            MainWindow = window;
            _activationWait=ThreadPool.RegisterWaitForSingleObject(_activation,(_,_)=>Dispatcher.BeginInvoke(new Action(window.Reveal)),null,Timeout.Infinite,false);
            window.InstallTray();
            if(!e.Args.Contains("--tray") || demo) window.Show();
            if(demo && e.Args.Contains("--verify-appearance-load"))
            {
                File.WriteAllText(Path.Combine(dataDir,"appearance-loaded.txt"),$"{ThemeManager.Current.Mode}|{ThemeManager.Current.Material}|{ThemeManager.Current.Accent}|dark={ThemeManager.IsDark}");
                ExitApp(); return;
            }
            if(Option("--capture") is { } capture && demo)
            {
                var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(650)};
                timer.Tick+=async (_,_)=> {
                    timer.Stop();
                    try { await window.PreparePolishCaptureAsync(capture); window.Capture(capture); await window.VerifyAiUiAsync(capture); }
                    catch(Exception ex) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(capture))!); File.WriteAllText(Path.ChangeExtension(capture,"error.txt"),ex.ToString()); }
                    finally { window.CloseTransientViews(); ExitApp(); }
                }; timer.Start();
            }
            if(int.TryParse(Option("--smoke-seconds"),out var seconds))
            {
                var timer=new DispatcherTimer {Interval=TimeSpan.FromSeconds(Math.Clamp(seconds,2,120))};
                timer.Tick+=(_,_)=> { timer.Stop(); _tracker?.Flush(); File.WriteAllText(Path.Combine(dataDir,"smoke-result.txt"),$"Status: {_tracker?.Status}\nForegroundSeconds: {_db.GetDay(DateOnly.FromDateTime(DateTime.Now),TimeZoneInfo.Local).Foreground.TotalSeconds:0.###}\n"); ExitApp(); }; timer.Start();
            }
            if(e.Args.Contains("--verify-flow") && Option("--data-dir") is not null && _tracker is not null)
                _ = VerifyFlowAsync(window,dataDir);
        }
        catch(Exception ex)
        {
            var captureIndex=Array.IndexOf(e.Args,"--capture");
            if(captureIndex>=0 && captureIndex+1<e.Args.Length)
            {
                var target=Path.ChangeExtension(Path.GetFullPath(e.Args[captureIndex+1]),"error.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllText(target,ex.ToString());
            }
            else MessageBox.Show("TraceTime 无法启动。\n"+ex.Message,"TraceTime",MessageBoxButton.OK,MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    internal void ExitApp() { Exiting=true; Shutdown(); }

    private async System.Threading.Tasks.Task VerifyFlowAsync(MainWindow window,string dataDir)
    {
        var lines=new System.Collections.Generic.List<string>();
        try
        {
            await System.Threading.Tasks.Task.Delay(2200);
            _tracker!.TogglePause(); _tracker.Flush();
            var date=DateOnly.FromDateTime(DateTime.Now);
            var before=_db!.GetDay(date,TimeZoneInfo.Local).Foreground;
            await System.Threading.Tasks.Task.Delay(2200); _tracker.Flush();
            if(_db.GetDay(date,TimeZoneInfo.Local).Foreground!=before) throw new Exception("暂停期间仍增长");
            lines.Add("PASS pause stops accumulation");
            _tracker.TogglePause(); await System.Threading.Tasks.Task.Delay(2200); _tracker.Flush();
            if((_db.GetDay(date,TimeZoneInfo.Local).Foreground-before).TotalSeconds is <1 or >4) throw new Exception("恢复后补记空档或未记录");
            lines.Add("PASS resume excludes pause gap");
            var previousSettings=_tracker.Settings; _tracker.UpdateSettings(new(2));
            if(_db.LoadSettings().IdleMinutes!=2) throw new Exception("设置未保存");
            _tracker.UpdateSettings(previousSettings); lines.Add("PASS settings persist and restore");
            window.Close(); if(window.IsVisible) throw new Exception("关闭后窗口未隐藏");
            var closedAt=_db.GetDay(date,TimeZoneInfo.Local).Foreground;
            await System.Threading.Tasks.Task.Delay(2200); _tracker.Flush();
            if(_db.GetDay(date,TimeZoneInfo.Local).Foreground<=closedAt) throw new Exception("关闭主窗口后停止采集");
            lines.Add("PASS close to tray keeps recording");
            window.Reveal(); if(!window.IsVisible) throw new Exception("无法唤起窗口");
            lines.Add("PASS reveal restores window");
            window.VerifyViewFlow(); lines.Add("PASS history, metric, timeline and preference views");
            window.VerifyAppearanceFlow(); lines.Add("PASS theme controls, palette contrast and appearance persistence");
        }
        catch(Exception ex) { lines.Add("FAIL "+ex); }
        finally { File.WriteAllLines(Path.Combine(dataDir,"flow-result.txt"),lines); ExitApp(); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _activationWait?.Unregister(null); _tracker?.Dispose(); (MainWindow as MainWindow)?.DisposeTray(); _db?.Dispose(); _activation?.Dispose(); _mutex?.Dispose();
        base.OnExit(e);
    }
}
