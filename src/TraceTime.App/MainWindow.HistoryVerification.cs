using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TraceTime.Core;
using TraceTime.Storage;

namespace TraceTime.App;

public partial class MainWindow
{
    private void VerifyHistoryQuickViews(string path,Action<string> save,Action<FrameworkElement,string> saveElement)
    {
        if(!_demo)throw new InvalidOperationException("只能在隔离演示中验证");
        static void Check(bool value,string message){if(!value)throw new Exception(message);}
        string Image(string name)=>Path.ChangeExtension(path,name+".png");
        ChangeAppearance(new("light","glass"),false);ShowOverview(this,new());Today(this,new());
        var originalPreferences=_viewPreferences;var total=ForegroundTotal.Text;var count=AppList.Items.Count;
        var app=_report!.Apps.First();AppSearch.Text=app.App.Name;
        Check(AppList.Items.Count>0&&AppList.Items.Count<count&&ForegroundTotal.Text==total,"搜索改变总计或未筛选");
        OverviewPanel.ScrollToTop();save(Image("search-overview"));
        AppSearch.Text="不存在的应用abcdef";Check(AppList.Items.Count==0&&AppSearchHint.Text.Contains("没有匹配"),"搜索空状态缺失");
        ClearAppSearch(this,new());Check(AppList.Items.Count==count,"清空搜索未恢复");
        PreviousRecorded(this,new());Check(SelectedDay<DateOnly.FromDateTime(DateTime.Today),"上个记录日未跳转");LatestRecorded(this,new());Check(SelectedDay==DateOnly.FromDateTime(DateTime.Today),"最近记录日错误");
        ShowLimits(this,new());LimitSearch.Text=app.App.Name;Check(LimitAppPicker.Items.Count==1,"额度应用搜索失败");
        LimitSearch.Text="不存在的规则abcdef";Check(!SaveLimitButton.IsEnabled&&NoRecordedApps.Visibility==Visibility.Visible,"搜索无匹配仍允许保存");
        ClearLimitSearch(this,new());LimitAppPicker.SelectedValue=app.App.Key;SaveLimitRule(this,new());var rule=_limits.Single(x=>x.AppKey==app.App.Key);
        var today=DateOnly.FromDateTime(DateTime.Today);var prior=_db.LoadLimitDay(rule,today);var rules=_limits.ToArray();
        ToggleQuietToday(this,new());Check(_db.IsReminderQuiet(today)&&QuietStatus.Visibility==Visibility.Visible&&rules.SequenceEqual(_db.LoadLimits())&&_db.LoadLimitDay(rule,today)==prior,"全局静默改写规则或独立状态");
        LimitSearch.Text=app.App.Name;RenderLimitProgress();LimitsPanel.ScrollToBottom();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);save(Image("quiet-rules"));ClearLimitSearch(this,new());ToggleQuietToday(this,new());RenderLimitProgress();Check(!_db.IsReminderQuiet(today),"无法恢复今日提醒");
        SaveViewPreferences(_viewPreferences with {TemporaryMinutes=25});
        var dialog=new TemporaryUseWindow(app.App.Name,minutes=>StartTemporaryUse(rule,minutes),_viewPreferences.TemporaryMinutes,minutes=>SaveViewPreferences(_viewPreferences with {TemporaryMinutes=minutes})){ShowActivated=false};
        dialog.Show();dialog.UpdateLayout();saveElement((FrameworkElement)dialog.Content,Image("temporary-preference"));
        Check(dialog.MinutesInput.Text=="25","未恢复常用分钟");dialog.MinutesInput.Text="20";dialog.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(ViewPreferences.Load(_viewPreferencesPath).TemporaryMinutes==20,"常用分钟未保存");
        PresentLimitAlert(app.App.Name,rule,today,3600);Check(_limitAlert!.SnoozeButton.Content?.ToString()=="临时使用 20 分钟","提醒按钮未使用偏好");CloseLimitAlert();
        ShowSettings(this,new());SettingsPanel.ScrollToBottom();UpdateLayout();save(Image("history-settings"));
        Check(ArchiveMonth.SelectedDate.HasValue&&RemoveArchivedMonth.IsChecked!=true&&HistoryActions.IsEnabled&&HistoryStorageText.Text.Contains("iB"),"历史管理默认状态不正确");
        SetHistoryBusy(true,"隔离测试维护状态");Check(!SettingsPanel.IsEnabled&&!PauseButton.IsEnabled,"维护期间仍可修改历史或暂停状态");SetHistoryBusy(false,"");Check(SettingsPanel.IsEnabled,"维护结束未恢复操作");
        Width=900;Height=680;ChangeAppearance(new("dark","glass"),false);HistoryStorageText.BringIntoView();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UpdateLayout();save(Image("history-compact-dark"));
        Width=1160;Height=860;
        var folder=Path.Combine(Path.GetDirectoryName(path)!,"history-ui");Directory.CreateDirectory(folder);
        using(var db=new UsageDatabase(Path.Combine(folder,"usage.db")))
        {
            var start=ReportBuilder.Bounds(today,TimeZoneInfo.Local).Start.AddHours(2);db.Save(new([new("history","history",new("test","历史验证"),start,start.AddMinutes(5))],[]));
            var archive=UsageDatabase.CreateHistoryArchive(db.FilePath,Path.Combine(folder,"history.ttarchive"),TimeZoneInfo.Local);
            Check(ArchiveSummary(archive).Contains("应用时间片 1"),"恢复预览没有数量");
            db.RestoreHistoryArchive(archive.Path,archive.Sha256,Path.Combine(folder,"safety.ttarchive"),today,TimeZoneInfo.Local);
            Check(db.IsReminderQuiet(today)&&File.Exists(Path.Combine(folder,"safety.ttarchive")),"恢复未设置静默或缺安全备份");
            File.WriteAllText(Path.ChangeExtension(path,"archive-summary.txt"),ArchiveSummary(archive));
        }
        SaveViewPreferences(originalPreferences);CancelTemporaryUse(new Button{Tag=rule.AppKey},new());ChangeAppearance(new("light","glass"),false);ShowOverview(this,new());
        File.WriteAllText(Path.ChangeExtension(path,"history-quick.txt"),"PASS app/limit search and empty/clear, totals unchanged, recorded-date jumps, global quiet/revert without rule mutation, temporary preference and reminder action, archive defaults/storage size/compact dark view, restore summary and safety snapshot\n");
    }
}
