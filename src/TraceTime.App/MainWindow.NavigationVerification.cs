using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using TraceTime.Core;

namespace TraceTime.App;

public partial class MainWindow
{
    private void VerifyNavigationViews(string path, Action<string> save)
    {
        if (!_demo) throw new InvalidOperationException("仅隔离演示允许验证。");
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        void Layout() { UpdateLayout(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); }
        var original = _viewPreferences;
        ShowOverview(this,new()); Today(this,new()); Width=1160; Height=860;
        TimelineExpander.IsExpanded = false;
        SaveViewPreferences(original with { SidebarCollapsed = false, DayChart = "bar" });

        // A long ranking must remain complete and scroll with the outer page.
        var realReport = _report;
        var apps = Enumerable.Range(0,45).Select(i => new AppUsage(new AppIdentity("scroll-"+i,"滚动验证应用 "+i),TimeSpan.FromMinutes(50-i),TimeSpan.FromMinutes(30))).ToArray();
        _report = new DayReport(SelectedDay, apps, []); RenderRows(); Layout();
        var inner = VisualChildren(AppList).OfType<ScrollViewer>().First();
        Check(AppList.Items.Count==45 && inner.ScrollableHeight==0 && OverviewPanel.ScrollableHeight>0,$"长应用排行检查失败：行数={AppList.Items.Count}，内层滚动={inner.ScrollableHeight}，页面滚动={OverviewPanel.ScrollableHeight}");
        OverviewPanel.ScrollToVerticalOffset(120); Layout();
        var offset = OverviewPanel.VerticalOffset;
        var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,-120) { RoutedEvent=Mouse.PreviewMouseWheelEvent };
        ((ListBoxItem)AppList.ItemContainerGenerator.ContainerFromIndex(0)).RaiseEvent(wheel); Layout();
        Check(wheel.Handled && (SystemParameters.WheelScrollLines==0 || OverviewPanel.VerticalOffset>offset) && inner.VerticalOffset==0,"排行上的滚轮未滚动整页");
        _report=realReport;RenderRows();RenderTimeline();

        TimelineExpander.IsExpanded=true;OpenDayDistribution(this,new());Layout();
        offset=OverviewPanel.VerticalOffset;
        wheel=new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,120) {RoutedEvent=Mouse.PreviewMouseWheelEvent};
        DetailsList.RaiseEvent(wheel);Layout();
        Check(wheel.Handled && (SystemParameters.WheelScrollLines==0 || OverviewPanel.VerticalOffset<offset),"时间片上的滚轮仍被内部列表吞掉");

        SaveViewPreferences(_viewPreferences with {SidebarCollapsed=true,DayChart="timeline"});Layout();
        Check(SidebarColumn.ActualWidth==72 && BrandLabel.Visibility==Visibility.Collapsed && ViewPreferences.Load(_viewPreferencesPath)==_viewPreferences,"侧栏和图表偏好保存失败");
        foreach(var (nav,panel) in new[]{(LimitsNav,LimitsPanel),(AnalysisNav,AnalysisPanel),(AppearanceNav,AppearancePanel),(SettingsNav,SettingsPanel),(OverviewNav,OverviewPanel)})
        {
            nav.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout();
            Check(panel.Visibility==Visibility.Visible && !string.IsNullOrWhiteSpace(nav.ToolTip as string),"图标侧栏导航或名称提示失效");
        }
        TimelineExpander.IsExpanded=false;OverviewPanel.ScrollToTop();save(Path.ChangeExtension(path,"sidebar-compact.png"));
        Width=900;Height=620;OpenDayDistribution(this,new());Layout();save(Path.ChangeExtension(path,"sidebar-chart.png"));
        ChangeAppearance(new("dark","glass"),false);save(Path.ChangeExtension(path,"sidebar-dark.png"));
        SidebarToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout();
        Check(SidebarColumn.ActualWidth==200 && BrandLabel.IsVisible,"侧栏不能重新展开");
        Width=1160;Height=860;ChangeAppearance(new("light","glass"),false);
        SaveViewPreferences(original);ShowOverview(this,new());TimelineExpander.IsExpanded=false;OverviewPanel.ScrollToTop();
        save(Path.ChangeExtension(path,"overview-final.png"));
        File.WriteAllText(Path.ChangeExtension(path,"navigation.txt"),"PASS 45 application rows have no inner scroll; routed wheel over ranking/details scrolls the page; all five icon destinations, sidebar collapse/expand, preferences persistence, compact and dark views.");
    }
}
