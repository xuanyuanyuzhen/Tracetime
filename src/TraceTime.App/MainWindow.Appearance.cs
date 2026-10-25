using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace TraceTime.App;

public partial class MainWindow
{
    private readonly string _appearancePath;
    private AppearanceSettings _appearance;
    private bool _syncingAppearance;
    private string _page = "overview";

    private void SetPage(string page)
    {
        var changed = _page != page;
        _page = page;
        OverviewPanel.Visibility = page == "overview" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        AppearancePanel.Visibility = page == "appearance" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (button, name) in new[] { (OverviewNav,"overview"), (AppearanceNav,"appearance"), (SettingsNav,"settings") })
            button.SetResourceReference(BackgroundProperty, name == page ? "AccentSoft" : "Sidebar");
        PageTitle.Text = page switch { "appearance" => "外观与主题", "settings" => "设置与数据", _ => "每日概览" };
        PageSubtitle.Text = page switch { "appearance" => "让记录时间的地方，更像你。", "settings" => "按你的习惯记录，数据由你掌握。", _ => "看见时间的去向。" };
        if (changed && SystemParameters.ClientAreaAnimation && !_demo)
        {
            var panel = page == "appearance" ? AppearancePanel : page == "settings" ? SettingsPanel : OverviewPanel;
            panel.BeginAnimation(OpacityProperty, new DoubleAnimation(.7,1,TimeSpan.FromMilliseconds(140)) { FillBehavior = FillBehavior.Stop });
        }
    }
    private void ShowAppearance(object sender, RoutedEventArgs e) => SetPage("appearance");

    private void SyncAppearanceControls()
    {
        _syncingAppearance = true;
        try
        {
            SystemMode.IsChecked = _appearance.Mode == "system"; LightMode.IsChecked = _appearance.Mode == "light"; DarkMode.IsChecked = _appearance.Mode == "dark";
            GlassStyle.IsChecked = _appearance.Material == "glass"; SoftStyle.IsChecked = _appearance.Material == "soft"; MinimalStyle.IsChecked = _appearance.Material == "minimal";
            var name = "自定义";
            foreach (RadioButton button in Swatches.Children)
            {
                button.IsChecked = string.Equals(button.Tag as string, _appearance.Accent, StringComparison.OrdinalIgnoreCase);
                if (button.IsChecked == true) name = (string)button.ToolTip;
            }
            HexInput.Text = _appearance.Accent; AccentName.Text = name;
            QuickThemeLabel.Text = ThemeManager.IsDark ? "切换浅色" : "切换深色";
            var mode = _appearance.Mode switch { "system" => "跟随系统", "dark" => "深色", _ => "浅色" };
            var material = _appearance.Material switch { "glass" => "液态玻璃", "soft" => "柔和", _ => "极简" };
            ThemeSummary.Text = mode + " · " + material;
        }
        finally { _syncingAppearance = false; }
    }

    private void ChangeAppearance(AppearanceSettings value, bool save = true)
    {
        var normalized = value.Normalize();
        try
        {
            // Save before changing the view: a failed write cannot silently look persisted.
            if (save) normalized.Save(_appearancePath);
            _appearance = normalized; ThemeManager.Apply(_appearance, this);
            SyncAppearanceControls(); SetPage(_page); RenderRows(); RenderTimeline(); UpdateStatus();
            AppearanceFeedback.Text = save ? "已保存。下次打开会恢复当前外观。" : "选择后立即生效，自动保存。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SyncAppearanceControls(); AppearanceFeedback.Text = "外观未保存，请检查数据文件夹权限后重试：" + ex.Message;
        }
    }

    private void ModeChanged(object sender, RoutedEventArgs e) { if (_ready && !_syncingAppearance && sender is RadioButton { Tag: string mode }) ChangeAppearance(_appearance with { Mode = mode }); }
    private void MaterialChanged(object sender, RoutedEventArgs e) { if (_ready && !_syncingAppearance && sender is RadioButton { Tag: string material }) ChangeAppearance(_appearance with { Material = material }); }
    private void AccentChanged(object sender, RoutedEventArgs e) { if (_ready && !_syncingAppearance && sender is RadioButton { Tag: string accent }) ChangeAppearance(_appearance with { Accent = accent }); }
    private void ToggleTheme(object sender, RoutedEventArgs e) => ChangeAppearance(_appearance with { Mode = ThemeManager.IsDark ? "light" : "dark" });
    private void ResetAppearance(object sender, RoutedEventArgs e) => ChangeAppearance(new());
    private void ApplyHex(object sender, RoutedEventArgs e)
    {
        var value = HexInput.Text.Trim();
        if (!AppearanceSettings.ValidAccent(value)) { AppearanceFeedback.Text = "请输入有效的六位色值，例如 #24777B。"; HexInput.Focus(); return; }
        ChangeAppearance(_appearance with { Accent = value });
    }
    private void HexKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { ApplyHex(sender, e); e.Handled = true; } }
    private void ChooseColor(object sender, RoutedEventArgs e)
    {
        using var picker = new Forms.ColorDialog { FullOpen = true, Color = System.Drawing.ColorTranslator.FromHtml(_appearance.Accent) };
        if (picker.ShowDialog(new DialogOwner(new WindowInteropHelper(this).Handle)) == Forms.DialogResult.OK)
            ChangeAppearance(_appearance with { Accent = $"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}" });
    }
    private sealed class DialogOwner(IntPtr handle) : Forms.IWin32Window { public IntPtr Handle => handle; }
    private void SystemThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Dispatcher.HasShutdownStarted) return;
        Dispatcher.BeginInvoke(new Action(() => { if (_ready) ChangeAppearance(_appearance, false); }));
    }

    internal void VerifyAppearanceFlow()
    {
        var previous = _appearance;
        var trackingSettings = _db.LoadSettings();
        var report = _db.GetDay(SelectedDay, TimeZoneInfo.Local);
        try
        {
            ShowAppearance(this, new()); DarkMode.IsChecked = true; MinimalStyle.IsChecked = true;
            HexInput.Text = "#bad"; ApplyHex(this, new());
            if (!AppearanceFeedback.Text.Contains("有效")) throw new Exception("非法自定义色值未报告");
            HexInput.Text = "#7956B3"; ApplyHex(this, new());
            var loaded = AppearanceSettings.Load(_appearancePath);
            if (loaded != new AppearanceSettings("dark", "minimal", "#7956B3")) throw new Exception("外观选择未正确保存");
            if (!ThemeManager.IsDark || AppearancePanel.Visibility != Visibility.Visible) throw new Exception("主题或页面切换未生效");
            foreach (var mode in new[] { "light", "dark" })
            foreach (var material in AppearanceSettings.Materials)
            foreach (var accent in new[] { "#24777B", "#3564C0", "#7956B3", "#A64E6E", "#A56820", "#3F805C", "#FFFFFF", "#000000", "#FFFF00" })
            {
                ChangeAppearance(new(mode, material, accent), false);
                var surface = ((SolidColorBrush)ThemeManager.Get("Surface")).Color;
                foreach (var key in new[] { "Ink", "Muted", "Accent" })
                    if (ThemeManager.Contrast(((SolidColorBrush)ThemeManager.Get(key)).Color, surface) < 4.5) throw new Exception($"{mode}/{material}/{accent} 的 {key} 对比度不足");
                var selected = mode == "dark" ? DarkMode : LightMode;
                selected.ApplyTemplate();
                var selectedBox = (Border)selected.Template.FindName("Box", selected);
                if (selected.IsChecked != true || ThemeManager.Contrast(((SolidColorBrush)selected.Foreground).Color, ((SolidColorBrush)selectedBox.Background).Color) < 4.5)
                    throw new Exception($"{mode}/{material}/{accent} 的实际选中标签对比度不足");
            }
            ResetAppearance(this, new());
            if (AppearanceSettings.Load(_appearancePath) != new AppearanceSettings()) throw new Exception("恢复默认外观失败");
            if (_db.LoadSettings() != trackingSettings) throw new Exception("外观切换改变了计时设置");
            // In demo mode, records must be identical before and after appearance changes.
            if (_demo && _db.GetDay(SelectedDay, TimeZoneInfo.Local).Foreground != report.Foreground) throw new Exception("外观切换改变了时长");
        }
        finally { ChangeAppearance(previous); ShowOverview(this, new()); }
    }
}
