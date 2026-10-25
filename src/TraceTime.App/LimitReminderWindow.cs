using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using TraceTime.Core;

namespace TraceTime.App;

internal sealed class LimitReminderWindow : Window
{
    private bool _allowClose;
    internal Func<bool, bool>? Accept;
    internal Button ConfirmButton { get; }
    internal Button SnoozeButton { get; }
    internal LimitReminderWindow(string name, UsageLimit rule, double seconds)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "TraceTime · 时长提醒"; Width = 480; SizeToContent = SizeToContent.Height; MinHeight = 340; MaxHeight = SystemParameters.WorkArea.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true; ShowActivated = rule.Level == ReminderLevel.Severe; ShowInTaskbar = true;
        var panel = new StackPanel();
        var heading = new TextBlock { Text = rule.Level == ReminderLevel.Severe ? "先停一下，看看你的时间" : "已达到今天的应用额度", FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var detail = new TextBlock { Text = $"{name}\n今日{(rule.Metric == UsageMetric.Foreground ? "前台停留" : "扣除空闲后")} {MainWindow.FormatTime(TimeSpan.FromSeconds(seconds))}，你设定的额度为 {rule.Minutes} 分钟。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,18,0,12) };
        var hint = new TextBlock { Text = "你仍可以继续使用。请选择下面的操作关闭提醒；明天会重新计算。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0,0,0,20) }; hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        ConfirmButton = new Button { Content = "知道了，今天不再提醒", Margin = new Thickness(0,0,0,10), IsDefault = true };
        ConfirmButton.Style = (Style)Application.Current.FindResource("PrimaryButton");
        SnoozeButton = new Button { Content = "再用 10 分钟提醒" };
        ConfirmButton.Click += (_, _) => Answer(false); SnoozeButton.Click += (_, _) => Answer(true);
        panel.Children.Add(heading); panel.Children.Add(detail); panel.Children.Add(hint); panel.Children.Add(ConfirmButton); panel.Children.Add(SnoozeButton);
        var surface = new Border { Padding = new Thickness(28), Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        surface.SetResourceReference(Border.BackgroundProperty,"Canvas"); Content = surface;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        Closing += (_, e) => { if (!_allowClose && !((App)Application.Current).Exiting) e.Cancel = true; };
    }
    private void Answer(bool snooze) { if (Accept?.Invoke(snooze) == false) return; _allowClose = true; Close(); }
    internal void DismissSilently() { _allowClose = true; Close(); }
    internal void RequestForeground()
    {
        Activate(); var handle = new WindowInteropHelper(this).Handle;
        if (!SetForegroundWindow(handle)) { var flash = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Hwnd = handle, Flags = 3, Count = 3 }; FlashWindowEx(ref flash); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct FlashInfo { public uint Size; public IntPtr Hwnd; public uint Flags; public uint Count; public uint Timeout; }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr handle);
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FlashInfo info);
}
