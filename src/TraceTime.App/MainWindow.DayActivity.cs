using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TraceTime.Core;

namespace TraceTime.App;

public partial class MainWindow
{
    private DayActivity? _dayActivity;
    private int _activityHour=-1;
    private bool _updatingActivity;
    private DateOnly? _activityDate;
    private sealed record PeriodOption(int Index,string Label);
    private void RenderActivity(DateTimeOffset? observedUntil = null)
    {
        if(_report is null) return;
        if(_activityDate!=_report.Date){_activityHour=-1;_activityDate=_report.Date;}
        var (_,end)=ReportBuilder.Bounds(_report.Date,TimeZoneInfo.Local);
        _dayActivity=new(_report,TimeZoneInfo.Local,observedUntil ?? (_demo?end:DateTimeOffset.UtcNow));
        if(_activityHour>=_dayActivity.Hours.Count) _activityHour=-1;
        _updatingActivity=true;
        ActivityPeriod.ItemsSource=new[]{new PeriodOption(-1,"全天")}.Concat(_dayActivity.Hours.Select(x=>new PeriodOption(x.Index,x.Label))).ToArray();
        ActivityPeriod.SelectedValue=_activityHour;_updatingActivity=false;
        Timeline.Activity=_dayActivity;Timeline.SelectedAppKey=_selectedApp;Timeline.SelectedHour=_activityHour;
        var whole=_dayActivity.Whole;
        ActivitySummary.Text=$"前台停留 {FormatTime(whole.Foreground)}（含空闲 {FormatTime(whole.Idle)}）\n已知中断 {FormatTime(whole.Interrupted)} · 未运行 / 未记录 {FormatTime(whole.Unrecorded)}";
        var slice=SelectedActivity();
        ActivityRangeSummary.Text=$"{(_activityHour<0?"全天":_dayActivity.Hours[_activityHour].Label)} · 应用使用 {FormatTime(slice.Active)} · 空闲 {FormatTime(slice.Idle)} · 中断 {FormatTime(slice.Interrupted)} · 未记录 {FormatTime(slice.Unrecorded)}";
        PeriodApps.ItemsSource=slice.Apps.Select(x=>new {Visual=_icons.Get(x.App),Name=x.App.Name,Foreground=FormatTime(x.Foreground),WithoutIdle=FormatTime(x.WithoutIdle)}).ToArray();
        PeriodAppsEmpty.Visibility=slice.Apps.Count==0?Visibility.Visible:Visibility.Collapsed;
        PeriodAppsEmpty.Text=slice.Future==slice.End-slice.Start?"这个时段尚未到来。":"该时段没有应用使用记录；请查看下方的中断或未记录空档。";
    }
    private ActivitySlice SelectedActivity()=>_activityHour>=0?_dayActivity!.Hours[_activityHour].Slice:_dayActivity!.Whole;
    private void ActivityPeriodChanged(object sender,SelectionChangedEventArgs e)
    {
        if(!_ready||_updatingActivity)return;_activityHour=ActivityPeriod.SelectedValue is int i?i:-1;RenderTimeline();
    }
    private void ActivityModeChanged(object sender,RoutedEventArgs e)
    {
        if(!_ready || _syncingViewPreferences)return;
        SaveViewPreferences(_viewPreferences with { DayChart=ActivityLine.IsChecked==true?"timeline":ActivityRing.IsChecked==true?"ring":"bar" });
    }
    private void ClearActivityPeriod(object sender,RoutedEventArgs e){_activityHour=-1;RenderTimeline();}
    private void OpenDayDistribution(object sender,RoutedEventArgs e)
    {
        TimelineExpander.IsExpanded=true;UpdateLayout();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => {
            UpdateLayout();
            OverviewPanel.ScrollToVerticalOffset(OverviewPanel.VerticalOffset+ActivityChartTypes.TranslatePoint(new Point(),OverviewPanel).Y);
        }));
    }
}
