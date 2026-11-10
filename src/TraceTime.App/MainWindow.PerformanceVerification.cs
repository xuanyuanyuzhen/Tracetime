using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using TraceTime.Core;

namespace TraceTime.App;

public partial class MainWindow
{
    private void VerifyPerformanceViews(string path)
    {
        if(!_demo)throw new InvalidOperationException("性能界面检查只允许隔离演示。");
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
        void Layout(){UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);}
        ShowOverview(this,new());TimelineExpander.IsExpanded=false;RefreshBackground();
        Check(_report is not null && _dayActivity is null && Timeline.Activity is null && DetailsList.ItemsSource is null,"收起的时间线仍构造派生视图");
        var actual = _report;
        var rowA = new AppUsage(new("perf-a","性能演示A"),TimeSpan.FromHours(3),TimeSpan.FromHours(2));
        var rowB = new AppUsage(new("perf-b","性能演示B"),TimeSpan.FromHours(1),TimeSpan.FromMinutes(30));
        _report = new(SelectedDay,[rowA,rowB],[]);RenderRows();Layout();
        var list = AppList.ItemsSource; var first = (AppRow)AppList.Items[0];
        AppList.SelectedItem=first;var container=AppList.ItemContainerGenerator.ContainerFromIndex(0);
        _report = new(SelectedDay,[rowA with { Foreground=TimeSpan.FromMinutes(185),WithoutIdle=TimeSpan.FromMinutes(125) },rowB],[]);RenderRows();Layout();
        Check(ReferenceEquals(list,AppList.ItemsSource) && ReferenceEquals(container,AppList.ItemContainerGenerator.ContainerFromIndex(0)) && AppList.SelectedItem==first,"稳定排序刷新重建行控件或丢失选择");
        Check(first.Duration.Contains("5 分") && VisualChildren(container).OfType<System.Windows.Controls.TextBlock>().Any(x=>x.Text==first.Duration),"复用行后时长绑定没有更新");
        _report=actual;_selectedApp=null;RenderRows();
        TimelineExpander.IsExpanded=true;Layout();
        Check(_dayActivity is not null && DetailsList.Items.Count>0,"展开时间线未恢复明细");
        var count=DetailsList.Items.Count;TimelineExpander.IsExpanded=false;
        Check(_dayActivity is null && PeriodApps.ItemsSource is null,"收起时间线没有释放视图引用");
        TimelineExpander.IsExpanded=true;Check(DetailsList.Items.Count==count,"重新展开丢失时间片");
        var prior=_report;WindowState=WindowState.Minimized;Layout();RefreshBackground();
        Check(!CanRefreshViews && ReferenceEquals(prior,_report),"最小化仍重建概览报告");
        WindowState=WindowState.Normal;Layout();
        Check(CanRefreshViews && !ReferenceEquals(prior,_report),"恢复窗口没有立即刷新报告");
        prior=_report;Hide();RefreshBackground();RefreshReport();
        Check(ReferenceEquals(prior,_report),"托盘隐藏仍读取概览");
        Show();RefreshReport();TimelineExpander.IsExpanded=false;
        var cold=new MainWindow(_db,null,true,deferOverview:true){ShowActivated=false};
        try
        {
            Check(cold._report is null && cold._dayActivity is null,"托盘冷启动提前读取报告");
            cold.RefreshBackground();Check(cold._report is null,"托盘后台刷新提前构造报告");
            cold.Show();cold.RefreshReport();
            Check(cold._report is not null && cold.AppList.Items.Count>0 && cold._dayActivity is null,"首次打开未加载排行或提前构造时间线");
        }
        finally{cold.DisposeTray();cold.Closing-=cold.WindowClosing;cold.Close();}
        File.WriteAllText(Path.ChangeExtension(path,"performance.txt"),"PASS deferred tray report, first-open ranking, stable ranking preserves row containers/selection and updates bound duration, collapsed timeline allocation avoidance and release, complete expand/re-expand, hidden/minimized report suppression and immediate restore. Tracking/reminder timers remain separate from view visibility. Isolated demo only.");
    }
}
