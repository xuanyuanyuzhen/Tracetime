using System;
using TraceTime.App.Windows;

namespace TraceTime.App;

public partial class MainWindow
{
    private void LoadStartupPreference()
    {
        StartupCheck.IsEnabled = _startup is not null && !_demo;
        StartupCheck.ToolTip = _startup is null
            ? "演示、独立数据和开发运行不修改开机启动设置。请运行完整发布包。"
            : "升级或移动文件夹后，已开启的启动项会接续到新位置。若在 Windows 启动应用中禁用，请在那里手动恢复。";
        try { StartupCheck.IsChecked = _startup?.IsEnabled == true; }
        catch
        {
            StartupCheck.IsEnabled = false;
            SettingsFeedback.Text = "无法读取开机启动设置，暂不修改该选项；其他记录偏好仍可保存。";
        }
    }

    internal void SynchronizeStartup()
    {
        if (_startup is null || _demo) return;
        try
        {
            var result = _startup.Synchronize();
            LoadStartupPreference();
            if (result == StartupMigration.Updated)
                SettingsFeedback.Text = "已将原有开机启动设置接续到当前版本。";
            else if (result == StartupMigration.NewerVersion)
                SettingsFeedback.Text = "开机启动指向更新的版本，已保留。如需切回本版本，请勾选并保存偏好。";
            else if (result == StartupMigration.Unrecognized)
                ShowError("已有同名启动项无法识别，已保留。请在 Windows 启动应用中检查 TraceTime。");
        }
        catch
        {
            ShowError("开机启动路径未能更新。请在「设置与数据」检查并重新保存启动选项。当前记录不受影响。");
        }
    }
}
