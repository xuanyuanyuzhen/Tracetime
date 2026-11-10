using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace TraceTime.App;

internal sealed class TemporaryUseWindow : Window
{
    internal TextBox MinutesInput { get; } = new() { Text = "10", MaxLength = 3, Width = 100, HorizontalAlignment = HorizontalAlignment.Left };
    internal Button StartButton { get; } = new() { Content = "开启临时使用", IsDefault = true };
    internal TextBlock Feedback { get; } = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0,10,0,10) };
    internal TemporaryUseWindow(string name, Action<int> start, int defaultMinutes = 10, Action<int>? saveDefault = null)
    {
        Title = "TraceTime · 临时使用"; Width = 480; SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        MinutesInput.Text = (defaultMinutes is >=1 and <=120 ? defaultMinutes : 10).ToString();
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "临时使用", FontSize = 24, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap, Margin = new(0,12,0,8) });
        var hint = new TextBlock { Text = "可以提前开启。只按这个应用的前台停留累计，切到后台、暂停或锁屏时不消耗；空闲仍会消耗。到期按原强度提醒，跨日结束。", TextWrapping = TextWrapping.Wrap, Margin = new(0,0,0,16) };
        hint.SetResourceReference(TextBlock.ForegroundProperty,"Muted"); panel.Children.Add(hint);
        var quick = new WrapPanel();
        foreach (var minutes in new[] {5,10,15,30})
        {
            var button = new Button { Content = $"{minutes} 分钟", Margin = new(0,0,8,10), Padding = new(10,8,10,8) };
            button.Click += (_, _) => MinutesInput.Text = minutes.ToString(); quick.Children.Add(button);
        }
        panel.Children.Add(quick); var label = new Label { Content = "使用分钟数（1–120）", Target = MinutesInput }; label.SetResourceReference(Label.ForegroundProperty,"Ink"); panel.Children.Add(label);
        AutomationProperties.SetName(MinutesInput,"临时使用分钟数，1至120"); panel.Children.Add(MinutesInput);
        var remember = new CheckBox { Content = "设为常用时长，下次默认使用", IsChecked = true, Margin = new(0,12,0,0), Visibility = saveDefault is null ? Visibility.Collapsed : Visibility.Visible };
        remember.SetResourceReference(CheckBox.ForegroundProperty,"Ink"); panel.Children.Add(remember);
        Feedback.SetResourceReference(TextBlock.ForegroundProperty,"Danger"); panel.Children.Add(Feedback);
        var actions = new WrapPanel(); StartButton.Style = (Style)Application.Current.FindResource("PrimaryButton");
        StartButton.Click += (_, _) => {
            if (!int.TryParse(MinutesInput.Text, out var minutes) || minutes is < 1 or > 120) { Feedback.Text = "请输入 1–120 之间的整数分钟。"; return; }
            try { start(minutes); if(remember.IsChecked==true) saveDefault?.Invoke(minutes); Close(); } catch (Exception ex) { Feedback.Text = "未能开启：" + ex.Message; }
        };
        actions.Children.Add(StartButton); var cancel = new Button { Content = "取消", IsCancel = true, Margin = new(10,0,0,0) }; cancel.Click += (_, _) => Close(); actions.Children.Add(cancel); panel.Children.Add(actions);
        var surface = new Border { Padding = new(26), Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        surface.SetResourceReference(Border.BackgroundProperty,"Canvas"); Content = surface;
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
    }
}
