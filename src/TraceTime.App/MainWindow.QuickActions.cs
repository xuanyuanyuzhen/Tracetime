using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TraceTime.Core;

namespace TraceTime.App;

public partial class MainWindow
{
    internal int PreferredTemporaryMinutes => _viewPreferences.TemporaryMinutes;
    private DateOnly? _previousRecordedDay, _nextRecordedDay;
    private void SearchApps(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        RenderRows(); RenderTimeline();
    }
    private void ClearAppSearch(object sender, RoutedEventArgs e) => AppSearch.Clear();
    private void SearchLimits(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        var selected = (LimitAppPicker.SelectedItem as AppVisual)?.Key;
        var choices = _knownLimitApps.Values.Where(x=>ApplicationQuery.Matches(x.Name,LimitSearch.Text)).OrderBy(x=>x.Name).Select(_icons.Get).ToArray();
        LimitAppPicker.ItemsSource = choices;
        LimitAppPicker.SelectedItem = choices.FirstOrDefault(x=>x.Key==selected) ?? choices.FirstOrDefault();
        SaveLimitButton.IsEnabled = choices.Length > 0;
        NoRecordedApps.Text = "没有匹配的应用，请修改或清空搜索。";
        NoRecordedApps.Visibility = choices.Length==0 ? Visibility.Visible : Visibility.Collapsed;
        RenderLimitProgress();
    }
    private void ClearLimitSearch(object sender, RoutedEventArgs e) => LimitSearch.Clear();
    private void UpdateRecordedDayButtons()
    {
        _previousRecordedDay = _db.FindRecordedDay(SelectedDay,-1,TimeZoneInfo.Local);
        _nextRecordedDay = _db.FindRecordedDay(SelectedDay,1,TimeZoneInfo.Local);
        PreviousRecordedButton.IsEnabled = _previousRecordedDay.HasValue;
        NextRecordedButton.IsEnabled = _nextRecordedDay is { } next && next <= DateOnly.FromDateTime(DateTime.Today);
    }
    private void PreviousRecorded(object sender, RoutedEventArgs e) { if(_previousRecordedDay is { } day)DayPicker.SelectedDate=day.ToDateTime(TimeOnly.MinValue); }
    private void NextRecorded(object sender, RoutedEventArgs e) { if(_nextRecordedDay is { } day)DayPicker.SelectedDate=day.ToDateTime(TimeOnly.MinValue); }
    private void LatestRecorded(object sender, RoutedEventArgs e)
    {
        var day = _db.FindRecordedDay(DateOnly.FromDateTime(DateTime.Today).AddDays(1),-1,TimeZoneInfo.Local);
        if(day.HasValue) { DayPicker.SelectedDate=day.Value.ToDateTime(TimeOnly.MinValue); RecordedJumpHint.Text=""; }
        else RecordedJumpHint.Text="尚无应用记录。";
    }
    private void ToggleQuietToday(object sender, RoutedEventArgs e)
    {
        try
        {
            var today=DateOnly.FromDateTime(DateTime.Today);var quiet=!_db.IsReminderQuiet(today);
            _db.SetReminderQuiet(today,quiet);
            if(quiet) { _db.CancelQueuedEmails("今日全部静默，已取消未发送邮件"); _emailService.CancelActive(); CloseLimitAlert(); CloseBreakAlert(); }
            RenderLimitProgress();
        }
        catch(Exception ex) { LimitFeedback.Text="今日静默未能保存："+ex.Message; }
    }
    private void RenderQuietState()
    {
        var quiet=_db.IsReminderQuiet(DateOnly.FromDateTime(DateTime.Today));
        _allRemindersQuiet = quiet; _quietStateDay = DateOnly.FromDateTime(DateTime.Today);
        QuietTodayButton.Content=quiet?"恢复今日提醒":"今日全部静默";
        QuietTodayHint.Text=quiet?"今天所有应用的本地、邮件及休息提醒已暂停，明天自动恢复。计时与临时额度继续累计。"
            :"适用于所有应用规则和休息提醒（包括搜索未显示的规则），不改每应用设置和已选择的今日忽略。";
        QuietStatus.Visibility=quiet?Visibility.Visible:Visibility.Collapsed;
    }
}
