using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using TraceTime.Core;

namespace TraceTime.App;

internal sealed record ReminderMessage(string Title, string Heading, string Name, string Detail, string Hint,
    string Confirm = "开始休息", string Snooze = "5 分钟后提醒", string Ignore = "今天不再提醒");

internal sealed class LimitReminderWindow : Window
{
    private bool _allowClose;
    internal Func<bool, bool>? Accept;
    internal Func<bool>? Acknowledge;
    internal Button ConfirmButton { get; }
    internal Button SnoozeButton { get; }
    internal Button IgnoreButton { get; }
    internal LimitReminderWindow(string name, UsageLimit rule, double seconds,AppVisual? visual = null,bool preview=false,bool temporaryExpired=false,int temporaryMinutes=10,ReminderMessage? message=null)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = preview?"TraceTime · 提醒效果预览":"TraceTime · 时长提醒"; Width = 480; SizeToContent = SizeToContent.Height; MinHeight = 340; MaxHeight = SystemParameters.WorkArea.Height;
        if(preview)_allowClose=true;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true; ShowActivated = rule.Level == ReminderLevel.Severe; ShowInTaskbar = true;
        var panel = new StackPanel();
        var heading = new TextBlock { Text = (preview?"效果预览 · ":"")+(temporaryExpired ? "临时使用已到期" : rule.Level == ReminderLevel.Severe ? "先停一下" : "已达到今天的应用额度"), FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        var appHeader=new StackPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(0,18,0,0)};
        appHeader.Children.Add(new AppIconView {Value=visual ?? new AppVisual(new(rule.AppKey,name)),Margin=new Thickness(0,0,10,0)});
        appHeader.Children.Add(new TextBlock {Text=name,VerticalAlignment=VerticalAlignment.Center,MaxWidth=340,TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=name});
        var detail = new TextBlock { Text = $"今日{(rule.Metric == UsageMetric.Foreground ? "前台停留" : "扣除空闲后")} {MainWindow.FormatTime(TimeSpan.FromSeconds(seconds))}，你设定的额度为 {rule.Minutes} 分钟。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,10,0,12) };
        var hint = new TextBlock { Text = preview?"这是预览。下面按钮只关闭预览，不修改额度或今日提醒状态。":rule.RepeatOnEntry?"确认后可继续使用；再次进入超时应用仍会提醒。临时使用按前台停留累计。":"确认后本次提醒结束；临时使用到期仍会提醒。临时使用按前台停留累计。", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0,0,0,20) }; hint.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        ConfirmButton = new Button { Content = "知道了", Margin = new Thickness(0,0,0,10), IsDefault = true };
        ConfirmButton.Style = (Style)Application.Current.FindResource("PrimaryButton");
        SnoozeButton = new Button { Content = $"临时使用 {temporaryMinutes} 分钟", Margin = new Thickness(0,0,0,10) };
        IgnoreButton = new Button { Content = "今天不再提醒" };
        if (message is not null)
        {
            Title = preview ? "TraceTime · 休息提醒预览" : message.Title; heading.Text = (preview ? "效果预览 · " : "") + message.Heading;
            appHeader.Children.Clear(); appHeader.Children.Add(new TextBlock { Text = message.Name, TextWrapping = TextWrapping.Wrap });
            detail.Text = message.Detail; hint.Text = preview ? "这是预览，下面按钮只关闭预览，不改变休息计时或今日提醒状态。" : message.Hint;
            ConfirmButton.Content = message.Confirm; SnoozeButton.Content = message.Snooze; IgnoreButton.Content = message.Ignore;
        }
        ConfirmButton.Click += (_, _) => { if (Acknowledge is null) Answer(false); else if (Acknowledge()) { _allowClose=true; Close(); } };
        SnoozeButton.Click += (_, _) => Answer(true); IgnoreButton.Click += (_, _) => Answer(false);
        panel.Children.Add(heading); panel.Children.Add(appHeader); panel.Children.Add(detail); panel.Children.Add(hint); panel.Children.Add(ConfirmButton); panel.Children.Add(SnoozeButton); panel.Children.Add(IgnoreButton);
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
