using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TraceTime.Core;

namespace TraceTime.App;

public partial class MainWindow
{
    private void VerifyChartResourceViews(string path, Action<string> save, Action<FrameworkElement, string> saveElement)
    {
        if (!_demo) throw new InvalidOperationException("只允许在隔离演示模式验证。");
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        void Layout() { UpdateLayout(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); }
        void AnalysisTotal() => AnalysisTotals.Text = $"前台停留 {FormatTime(_analysis!.Foreground)}   ·   扣除空闲后 {FormatTime(_analysis.WithoutIdle)}";
        string Image(string name) => Path.ChangeExtension(path, name + ".png");
        Point Inside(Geometry shape)
        {
            var b = shape.Bounds;
            for (var y = b.Top + 1; y < b.Bottom; y += Math.Max(1, b.Height / 30))
                for (var x = b.Left + 1; x < b.Right; x += Math.Max(1, b.Width / 30))
                    if (shape.FillContains(new Point(x, y))) return new(x, y);
            throw new Exception("图表命中区域没有内部点");
        }
        void Hover(HoverChart chart, int index)
        {
            var region = chart.HoverRegions[index];
            chart.UpdateHoverAt(Inside(region.Shape)); Layout();
            Check(chart.IsHoverOpen && chart.HoverText == region.Text, "图表未显示命中数据：" + chart.GetType().Name);
        }

        var preferences = _viewPreferences;
        var appearance = _appearance;
        var originalReport = _report;
        Width = 1160; Height = 860;
        ChangeAppearance(new("light", "glass"), false);
        SaveViewPreferences(preferences with { SidebarCollapsed = false });
        ShowAnalysis(this, new()); Analysis7.IsChecked = true;
        var actualAnalysis = _analysis;
        var date = DateOnly.FromDateTime(DateTime.Today);
        var apps = Enumerable.Range(1, 9).Select(i => new AppUsage(new("hover-" + i, i == 9 ? "一个用于验证长应用名称截断与完整悬停文本的示例应用程序" : "演示应用 " + i),
            TimeSpan.FromSeconds(i * 801 + 7), TimeSpan.FromSeconds(i * 327 + 3))).ToArray();
        _analysis = new([new DayReport(date, apps, [])]);
        TrendChart.Analysis = _analysis; RenderAnalysisApps(); AnalysisTotal(); AnalysisPanel.ScrollToTop();
        TrendTab.IsChecked = true; Layout();
        Check(TrendChart.HoverRegions.Count == 1, "趋势柱图命中区域错误");
        Hover(TrendChart, 0);
        TrendChart.Analysis = new([new DayReport(date, [new(new("refresh", "刷新示例"), TimeSpan.FromSeconds(18061), TimeSpan.FromSeconds(9007))], [])]); Layout();
        Check(TrendChart.IsHoverOpen && TrendChart.HoverText!.Contains("5 小时 1 分 1 秒"), "静止指针下刷新未保持并更新提示");
        TrendChart.Analysis = _analysis; Layout();
        Check(TrendChart.HoverText!.Contains(ChartDetails.Duration(_analysis.Foreground)) && TrendChart.HoverText.Contains("秒"), "趋势柱图提示丢失精确时长");
        saveElement(TrendChart.HoverPopup!, Image("trend-tooltip")); TrendChart.CloseHover();
        TrendChart.UpdateHoverAt(new(1, 1)); Check(!TrendChart.IsHoverOpen, "趋势轴外仍显示旧提示");

        PieTab.IsChecked = true; AnalysisFg.IsChecked = true; Layout();
        Check(AnalysisPiePanel.IsVisible && !TrendPanel.IsVisible && AnalysisMetricPicker.IsVisible, "饼图切换或统计口径控件不可达");
        Check(AnalysisPie.HoverRegions.Count == 12, "饼图扇区和图例应各有六个区域");
        Hover(AnalysisPie, 0);
        var popup = AnalysisPie.HoverPopup; RenderAnalysisApps(); Layout();
        Check(AnalysisPie.IsHoverOpen && ReferenceEquals(popup, AnalysisPie.HoverPopup), "饼图刷新关闭或重建了悬停提示");
        Check(AnalysisPie.HoverText!.Contains("前台停留") && AnalysisPie.HoverText.Contains("分母"), "饼图缺双口径/分母");
        saveElement(AnalysisPie.HoverPopup!, Image("pie-tooltip")); AnalysisPie.CloseHover();
        save(Image("analysis-pie-light"));
        Hover(AnalysisPie, 10); Check(AnalysisPie.HoverText!.Contains("其余 4 个应用"), "其余分组提示错误"); AnalysisPie.CloseHover();
        AnalysisIdle.IsChecked = true; Layout(); Hover(AnalysisPie, 0);
        Check(AnalysisPie.HoverText!.Contains("扣除空闲后占比"), "切换口径后提示未更新"); AnalysisPie.CloseHover();
        AnalysisPie.UpdateHoverAt(new(1, 1)); Check(!AnalysisPie.IsHoverOpen, "饼图空白区残留提示");
        Hover(AnalysisPie, 0); TrendTab.IsChecked = true; Layout(); Check(!AnalysisPie.IsHoverOpen, "离开页面后提示未关闭");
        PieTab.IsChecked = true; Layout();
        AnalysisPie.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, Environment.TickCount, Key.Right) { RoutedEvent = Keyboard.KeyDownEvent });
        Check(AnalysisPie.IsHoverOpen, "键盘无法查看饼图数值");
        AnalysisPie.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
        Check(!AnalysisPie.IsHoverOpen, "Escape未关闭图表提示");
        ChangeAppearance(new("dark", "glass"), false); Width = 900; Height = 620;
        SaveViewPreferences(_viewPreferences with { SidebarCollapsed = true }); Layout();
        AnalysisPie.BringIntoView(); Layout();
        var pieTop = AnalysisPie.TranslatePoint(new(), AnalysisPanel).Y;
        Check(pieTop >= 0 && pieTop + AnalysisPie.ActualHeight <= AnalysisPanel.ViewportHeight + 1, "紧凑截图饼图未完整进入视口");
        save(Image("analysis-pie-compact-dark"));
        Check(ResourceUsage.IsVisible && ResourceUsage.ActualWidth > 0 && ResourceUsage.TranslatePoint(new(), (FrameworkElement)Content).Y + ResourceUsage.ActualHeight <= ((FrameworkElement)Content).ActualHeight, "紧凑侧栏资源信息超出窗口");

        _analysis = new([new DayReport(date, [new(new("one", "单个应用"), TimeSpan.FromSeconds(3723), TimeSpan.Zero)], [])]);
        AnalysisFg.IsChecked = true; RenderAnalysisApps(); AnalysisTotal(); Layout();
        Check(AnalysisPie.HoverRegions.Count == 2, "单应用没有画完整圆形"); Hover(AnalysisPie, 0);
        Check(AnalysisPie.HoverText!.Contains("1 小时 2 分 3 秒"), "精确时长格式错误"); AnalysisPie.CloseHover();
        AnalysisIdle.IsChecked = true; Layout();
        Check(AnalysisPie.HoverRegions.Count == 0, "零口径生成了伪造扇区"); save(Image("analysis-pie-empty"));

        ShowOverview(this, new()); Today(this, new()); TimelineExpander.IsExpanded = true;
        var bounds = ReportBuilder.Bounds(date, TimeZoneInfo.Local);
        _report = new(date, [new(new("sample", "演示阅读"), TimeSpan.FromSeconds(3667), TimeSpan.FromSeconds(3001))],
            [new(bounds.Start.AddHours(8), bounds.Start.AddHours(8).AddSeconds(3001), "sample", "演示阅读", "active"),
             new(bounds.Start.AddHours(8).AddSeconds(3001), bounds.Start.AddHours(8).AddSeconds(3667), "sample", "演示阅读", "idle")]);
        _activityHour = -1; RenderTimeline(bounds.Start.AddHours(12));
        SaveViewPreferences(_viewPreferences with { DayChart = "bar" });
        OpenDayDistribution(this, new()); Layout();
        Hover(Timeline, 8); Check(Timeline.HoverText!.Contains("50 分 1 秒"), "概览柱图秒级提示不准确");
        saveElement(Timeline.HoverPopup!, Image("day-bar-tooltip")); Timeline.CloseHover();
        SaveViewPreferences(_viewPreferences with { DayChart = "ring" }); Layout();
        Check(Timeline.HoverRegions.Count >= 7, "环图扇区/图例命中区域缺失"); Hover(Timeline, 0);
        Check(Timeline.HoverText!.Contains("50 分 1 秒") && Timeline.HoverText.Contains("不含未来"), "环图提示未保留数值或未来边界");
        saveElement(Timeline.HoverPopup!, Image("day-ring-tooltip")); Timeline.CloseHover();
        var ringCenter = new Point(Math.Min(75, Timeline.ActualWidth * .18) + 20, Timeline.ActualHeight / 2 - 6);
        Timeline.UpdateHoverAt(ringCenter); Check(!Timeline.IsHoverOpen, "环图中心误命中扇区");

        // Programmatic/mouse focus must not change the border; keyboard visuals remain available.
        ShowOverview(this, new()); TimelineExpander.IsExpanded = false;
        Width = 1160; Height = 860; SaveViewPreferences(_viewPreferences with { SidebarCollapsed = false });
        _report = originalReport; RefreshReport(); OverviewPanel.ScrollToTop(); Layout();
        SidebarToggle.Focus(); Layout();
        var surface = (Border)SidebarToggle.Template.FindName("Surface", SidebarToggle);
        Check(surface.BorderThickness == SidebarToggle.BorderThickness && SidebarToggle.FocusVisualStyle is not null, "鼠标/程序焦点仍触发加粗边框，或键盘焦点样式被移除");
        save(Image("sidebar-focus-dark"));
        Check(_resourceTimer?.IsEnabled == true, "可见窗口未启用资源采样");
        SampleResources(); Check(ResourceMemory.Text != "—", "未显示当前进程内存");
        Hide(); Layout(); Check(_resourceTimer?.IsEnabled == false, "隐藏到托盘后资源计时器仍运行");
        Show(); Layout(); Check(_resourceTimer?.IsEnabled == true && ResourceCpu.Text == "—", "重新显示未重建CPU基线");
        WindowState = WindowState.Minimized; Layout(); Check(_resourceTimer?.IsEnabled == false, "最小化后资源采样仍运行");
        WindowState = WindowState.Normal; Layout(); Check(_resourceTimer?.IsEnabled == true, "恢复窗口没有恢复采样");
        ChangeAppearance(new("light", "glass"), false); SampleResources(); save(Image("sidebar-resources-light"));

        _report = originalReport; _analysis = actualAnalysis; RefreshAnalysis();
        AnalysisFg.IsChecked = true; TrendTab.IsChecked = true; RenderAnalysisApps();
        SaveViewPreferences(preferences); ChangeAppearance(appearance, false); ShowOverview(this, new());
        File.WriteAllText(Path.ChangeExtension(path, "chart-resources.txt"), "PASS keyboard-only button focus style; analysis pie with metric/grouped/single/zero states; exact second trend/day-bar/ring tooltips, geometry hit/miss and popup dismissal; stationary hover survives redraw with updated values and reused popup; keyboard details; light/dark compact layouts and full chart visibility; current-process CPU/memory and hidden/minimized/resumed sampler lifecycle. No user data or system settings changed.");
    }
}
