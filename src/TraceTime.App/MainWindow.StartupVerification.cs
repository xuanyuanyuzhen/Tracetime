using System;
using System.IO;
using TraceTime.App.Windows;

namespace TraceTime.App;

public partial class MainWindow
{
    internal void VerifyStartupPreferences(string dataDir)
    {
        if (!_demo) throw new InvalidOperationException("启动项检查仅限隔离演示。");
        var initial = _db.LoadSettings();
        var store = new StartupVerificationStore(new("\"C:\\Apps\\Old\\TraceTime.exe\" --tray"));
        var startup = new StartupPreferences(store,@"C:\Apps\New\TraceTime.exe",new(0,8,2,0),_=>new(true,new(0,4,0,0)));
        var window = new MainWindow(_db,null,false,deferOverview:true,startup:startup);
        try
        {
            void Check(bool ok,string message) { if (!ok) throw new InvalidOperationException(message); }
            Check(window.StartupCheck.IsChecked == true,"旧路径未显示勾选");
            window.SynchronizeStartup();
            Check(store.Entry?.Command == "\"C:\\Apps\\New\\TraceTime.exe\" --tray" && window.SettingsFeedback.Text.Contains("接续"),"启动未接续或提示缺失");
            window.ShowSettings(this,new());
            Check(window.StartupCheck.IsChecked == true,"进入设置丢失状态");
            window.StartupCheck.IsChecked=false; window.IdleInput.Text="7"; window.SavePreferences(this,new());
            Check(store.Entry is null && _db.LoadSettings().IdleMinutes == 7,"取消启动与保存偏好未接入");
            window.StartupCheck.IsChecked=true; window.SavePreferences(this,new());
            Check(store.Entry is not null,"显式重新开启失败");
            store.FailWrite=true; window.StartupCheck.IsChecked=false; window.IdleInput.Text="8";
            window.SavePreferences(this,new());
            Check(window.SettingsFeedback.Text.StartsWith("保存失败") && _db.LoadSettings().IdleMinutes == 7 && store.Entry is not null,"失败被标记为成功");
            store.FailRead=true; window.ShowSettings(this,new());
            Check(!window.StartupCheck.IsEnabled,"读取失败后仍允许修改");
            window.IdleInput.Text="8";
            window.SavePreferences(this,new());
            Check(_db.LoadSettings().IdleMinutes == 8,"读取启动项失败阻止其他偏好保存");
            Check(!StartupCheck.IsEnabled,"演示窗口允许操作真实启动项");
            File.WriteAllText(Path.Combine(dataDir,"startup-result.txt"),"PASS old-path checkbox, successful startup migration, settings refresh, enable/disable, write/read failure and isolated-mode guard. In-memory startup store only; real Run and StartupApproved keys unchanged.");
        }
        finally
        {
            _db.SaveSettings(initial);
            window.DisposeTray(); window.Closing-=window.WindowClosing; window.Close();
        }
    }

    private sealed class StartupVerificationStore(StartupEntry? initial) : IStartupEntryStore
    {
        internal StartupEntry? Entry=initial;
        internal bool FailWrite,FailRead;
        public StartupEntry? Read() => FailRead ? throw new IOException("模拟读取失败") : Entry;
        public void Replace(StartupEntry? expected, StartupEntry? value)
        {
            if(FailWrite)throw new IOException("模拟写入失败");
            if(Entry!=expected)throw new InvalidOperationException("启动项改变");
            Entry=value;
        }
    }
}
