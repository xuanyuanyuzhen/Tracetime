using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TraceTime.Core;

namespace TraceTime.App;

public partial class MainWindow
{
    private IReadOnlyList<UsageLimit> _limits = [];
    private Dictionary<string, AppIdentity> _knownLimitApps = new();
    private LimitReminderWindow? _limitAlert;
    private UsageLimit? _alertRule;
    private DateOnly _alertDay;
    private DateOnly _prunedDay;
    private readonly LimitVisits _limitVisits = new();
    private bool _checkingLimits;

    private void ShowLimits(object sender, RoutedEventArgs e) { SetPage("limits"); RefreshRecordedApps(sender, e); RenderLimitProgress(); }
    private void RefreshRecordedApps(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = (LimitAppPicker.SelectedItem as AppVisual)?.Key;
            _tracker?.Flush(); var apps = _db.RecordedApps();
            _knownLimitApps = apps.ToDictionary(x => x.Key);
            var choices=apps.Where(x=>ApplicationQuery.Matches(x.Name,LimitSearch.Text)).Select(_icons.Get).ToArray();
            LimitAppPicker.ItemsSource = choices; LimitAppPicker.SelectedItem = choices.FirstOrDefault(x => x.Key == selected) ?? choices.FirstOrDefault();
            NoRecordedApps.Text = apps.Count == 0 ? "尚无应用记录。请先保持记录开启，打开目标应用并让它位于前台几秒。" : "没有匹配的应用，请修改或清空搜索。";
            NoRecordedApps.Visibility = choices.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            SaveLimitButton.IsEnabled = choices.Length > 0;
        }
        catch (Exception ex) { LimitFeedback.Text = "无法读取应用列表：" + ex.Message; }
    }
    private void SaveLimitRule(object sender, RoutedEventArgs e)
    {
        if (LimitAppPicker.SelectedItem is not AppVisual choice) { LimitFeedback.Text = "请先打开目标应用并在前台记录几秒，再刷新选择。"; return; }
        var app=choice.App;
        if (!int.TryParse(LimitMinutes.Text, out var minutes) || minutes is < 1 or > 1440) { LimitFeedback.Text = "每日额度须为 1–1440 之间的整数分钟。"; return; }
        try
        {
            if (_limits.Count >= 100 && _limits.All(x => x.AppKey != app.Key)) throw new InvalidOperationException("为保持轻量，最多设置 100 个应用额度。");
            _emailService.CancelRule(app.Key);
            _db.SaveLimit(new(app.Key, minutes, LimitWithoutIdle.IsChecked == true ? UsageMetric.WithoutIdle : UsageMetric.Foreground, LevelSevere.IsChecked == true ? ReminderLevel.Severe : LevelMedium.IsChecked == true ? ReminderLevel.Medium : ReminderLevel.Light,
                RepeatOnEntry: RepeatEntryCheck.IsChecked == true, EmailEnabled: RuleEmailCheck.IsChecked == true));
            _limits = _db.LoadLimits(); CloseLimitAlert(app.Key); RenderLimitProgress();
            LimitFeedback.Text = $"已保存 {app.Name} 的每日额度。保存后重新判断今天累计时长，已到额度会触发提醒。";
        }
        catch (Exception ex) { LimitFeedback.Text = "保存失败：" + ex.Message; }
    }
    private void ClearLimitEditor(object sender, RoutedEventArgs e) { LimitMinutes.Text = "60"; LimitForeground.IsChecked = LevelLight.IsChecked = RepeatEntryCheck.IsChecked = true; RuleEmailCheck.IsChecked = false; LimitFeedback.Text = ""; }
    private void EditLimitRule(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key || _limits.FirstOrDefault(x => x.AppKey == key) is not { } rule) return;
        RefreshRecordedApps(sender, e); LimitAppPicker.SelectedValue = key;
        LimitMinutes.Text = rule.Minutes.ToString(); LimitForeground.IsChecked = rule.Metric == UsageMetric.Foreground; LimitWithoutIdle.IsChecked = rule.Metric == UsageMetric.WithoutIdle;
        LevelLight.IsChecked = rule.Level == ReminderLevel.Light; LevelMedium.IsChecked = rule.Level == ReminderLevel.Medium; LevelSevere.IsChecked = rule.Level == ReminderLevel.Severe;
        RepeatEntryCheck.IsChecked = rule.RepeatOnEntry; RuleEmailCheck.IsChecked = rule.EmailEnabled;
        LimitsPanel.ScrollToTop(); LimitFeedback.Text = "编辑后点击保存。重新保存会重置今天的提醒状态。";
    }
    private void ToggleLimitRule(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key || _limits.FirstOrDefault(x => x.AppKey == key) is not { } rule) return;
        try { _emailService.CancelRule(key); _db.SetLimitEnabled(key, !rule.Enabled); _limits = _db.LoadLimits(); CloseLimitAlert(key); RenderLimitProgress(); }
        catch (Exception ex) { LimitFeedback.Text = ex.Message; }
    }
    private void RemoveLimitRule(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key) return;
        try { _emailService.CancelRule(key); _db.DeleteLimit(key); _limits = _db.LoadLimits(); CloseLimitAlert(key); RenderLimitProgress(); LimitFeedback.Text = "已删除额度规则，使用记录保留。"; }
        catch (Exception ex) { LimitFeedback.Text = ex.Message; }
    }
    private void IgnoreLimitToday(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key || _limits.FirstOrDefault(x => x.AppKey == key) is not { } rule) return;
        try { _emailService.CancelRule(key); _db.SaveLimitDay(LimitPolicy.Dismiss(_db.LoadLimitDay(rule, DateOnly.FromDateTime(DateTime.Today)))); CloseLimitAlert(key); RenderLimitProgress(); }
        catch (Exception ex) { LimitFeedback.Text = ex.Message; }
    }
    private void RefreshLimitProgress(object sender, RoutedEventArgs e) { _tracker?.Flush(); RenderLimitProgress(); }
    private void RenderLimitProgress()
    {
        if (!_ready) return;
        try
        {
            var day = DateOnly.FromDateTime(DateTime.Today); var report = _db.GetDay(day, TimeZoneInfo.Local);
            var quiet = _db.IsReminderQuiet(day);
            foreach(var app in report.Apps) _knownLimitApps[app.App.Key] = app.App;
            LimitRulesList.ItemsSource = _limits.Where(rule=>ApplicationQuery.Matches(_knownLimitApps.GetValueOrDefault(rule.AppKey)?.Name ?? "已删除使用记录的应用",LimitSearch.Text)).Select(rule => {
                var seconds = LimitPolicy.Seconds(report.Apps.FirstOrDefault(x => x.App.Key == rule.AppKey), rule.Metric);
                var state = _db.LoadLimitDay(rule, day); var fraction = Math.Clamp(seconds / (rule.Minutes * 60d), 0, 1);
                return new {
                    Visual = _icons.Get(_knownLimitApps.GetValueOrDefault(rule.AppKey) ?? new(rule.AppKey,"已删除使用记录的应用")),
                    rule.AppKey, Name = _knownLimitApps.GetValueOrDefault(rule.AppKey)?.Name ?? "已删除使用记录的应用", LevelText = LevelName(rule.Level), ToggleText = rule.Enabled ? "暂停规则" : "启用规则",
                    ProgressText = $"{MetricName(rule.Metric)}  {FormatTime(TimeSpan.FromSeconds(seconds))} / {rule.Minutes} 分钟 · {seconds / (rule.Minutes * 60d):P0}",
                    Fraction = new GridLength(fraction, GridUnitType.Star), Rest = new GridLength(1 - fraction, GridUnitType.Star),
                    TemporaryText = LimitPolicy.TemporaryActive(state, LimitPolicy.Seconds(report.Apps.FirstOrDefault(x => x.App.Key == rule.AppKey), UsageMetric.Foreground)) ? "调整临时使用…" : "临时使用…",
                    TemporaryVisibility = state.TemporaryUntilForeground is not null ? Visibility.Visible : Visibility.Collapsed,
                    rule.Enabled,
                    StateText = quiet ? "今日全部静默；计时及临时额度照常累计" : LimitStateText(rule, state, seconds, LimitPolicy.Seconds(report.Apps.FirstOrDefault(x => x.App.Key == rule.AppKey), UsageMetric.Foreground)),
                    OptionsText = (rule.RepeatOnEntry ? "再次进入提醒：开" : "再次进入提醒：关") + " · " + (rule.EmailEnabled ? "邮件：" + (_emailService.Settings.Enabled ? "开" : "等待全局开启") : "邮件：关")
                };
            }).ToArray();
            LimitsEmpty.Visibility = _limits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            RenderEmailStatus();
            RenderQuietState();
        }
        catch (Exception ex) { LimitFeedback.Text = "读取进度失败：" + ex.Message; }
    }
    private static string LevelName(ReminderLevel level) => level switch { ReminderLevel.Light => "轻度", ReminderLevel.Medium => "中度", _ => "严重" };
    private static string MetricName(UsageMetric metric) => metric == UsageMetric.Foreground ? "前台停留" : "扣除空闲后";

    private void CheckLimits()
    {
        if (_demo || _tracker is null || !_ready || _checkingLimits || _historyBusy) return;
        if(_previewOpen)return;
        ObserveLimitVisit();
        if (!_tracker.CanPresentReminder) { _limitAlert?.Hide(); return; }
        _checkingLimits = true;
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (_db.IsReminderQuiet(today)) { CloseLimitAlert(); return; }
            if (_limitAlert is not null && _alertDay != today) CloseLimitAlert();
            if (!_limits.Any(x => x.Enabled)) return;
            if (_prunedDay != today) { _db.PruneLimitDays(today); _prunedDay = today; }
            _tracker.Flush(); if (_tracker.Error is not null) return;
            var report = _db.GetDay(today, TimeZoneInfo.Local);
            QueueDueEmails(report);
            _ = DrainEmailQueueAsync(today);
            var next = LimitDispatch.NextForVisit(_limits,report,rule=>_db.LoadLimitDay(rule,today),_limitVisits,_tracker.CanPresentReminder);
            if (_limitAlert is not null && (_alertRule?.AppKey == next?.Rule.AppKey || _alertRule?.Level == ReminderLevel.Severe || next?.Rule.Level!=ReminderLevel.Severe))
            { if (!_limitAlert.IsVisible) _limitAlert.Show(); return; }
            if (next is not null)
            {
                if (_limitAlert is not null) CloseLimitAlert();
                var rule=next.Rule; var state=next.State; var app=next.Usage; var seconds=next.Seconds;
                var expired = LimitPolicy.TemporaryExpired(state, app.Foreground.TotalSeconds) && !state.TemporaryNotified;
                state = LimitPolicy.BeginPresentation(state, app.Foreground.TotalSeconds, seconds);
                _db.SaveLimitDay(state); _limitVisits.Presented(rule, today);
                if (rule.Level == ReminderLevel.Light)
                {
                    _tray?.ShowBalloonTip(6000, expired ? "TraceTime · 临时使用已到期" : "TraceTime · 每日额度提醒", expired ? $"{app.App.Name} 的临时使用额度已用完。" : $"{app.App.Name} 今日{MetricName(rule.Metric)}达到 {rule.Minutes} 分钟。可以休息一下。", System.Windows.Forms.ToolTipIcon.Info);
                    PlayLimitSound(rule.Level);
                    _db.SaveLimitDay(LimitPolicy.Acknowledge(state, seconds));
                    return;
                }
                PresentLimitAlert(app.App.Name, rule, today, seconds, expired);
                PlayLimitSound(rule.Level);
            }
        }
        catch (Exception ex) { ShowError("应用额度检查失败：" + ex.Message); }
        finally { _checkingLimits = false; }
    }
    private void PresentLimitAlert(string name, UsageLimit rule, DateOnly day, double seconds, bool temporaryExpired = false)
    {
        _alertRule = rule; _alertDay = day;
        if (rule.Level == ReminderLevel.Severe) { Reveal(); ShowLimits(this,new()); }
        var temporaryMinutes = _viewPreferences.TemporaryMinutes;
        var dialog = new LimitReminderWindow(name, rule, seconds,_icons.Get(_knownLimitApps.GetValueOrDefault(rule.AppKey) ?? new(rule.AppKey,name)),temporaryExpired:temporaryExpired,temporaryMinutes:temporaryMinutes); _limitAlert = dialog;
        dialog.Accept = snooze => {
            try
            {
                var current = _limits.FirstOrDefault(x => x.AppKey == rule.AppKey);
                if (DateOnly.FromDateTime(DateTime.Today) == day && current?.Revision == rule.Revision)
                {
                    _tracker?.Flush(); var report = _db.GetDay(day, TimeZoneInfo.Local);
                    var foreground = LimitPolicy.Seconds(report.Apps.FirstOrDefault(x => x.App.Key == rule.AppKey), UsageMetric.Foreground);
                    var state = _db.LoadLimitDay(rule, day);
                    _db.SaveLimitDay(snooze ? LimitPolicy.StartTemporary(state, foreground, temporaryMinutes, Guid.NewGuid().ToString("N")) : LimitPolicy.Dismiss(state));
                    if (!snooze) _emailService.CancelRule(rule.AppKey);
                }
                RenderLimitProgress(); return true;
            }
            catch (Exception ex) { ShowError("提醒状态保存失败，请重试：" + ex.Message); return false; }
        };
        dialog.Acknowledge = () => {
            try
            {
                var current = _limits.FirstOrDefault(x => x.AppKey == rule.AppKey);
                if (DateOnly.FromDateTime(DateTime.Today) == day && current?.Revision == rule.Revision)
                {
                    _tracker?.Flush(); var report = _db.GetDay(day, TimeZoneInfo.Local);
                    var used = LimitPolicy.Seconds(report.Apps.FirstOrDefault(x => x.App.Key == rule.AppKey), rule.Metric);
                    _db.SaveLimitDay(LimitPolicy.Acknowledge(_db.LoadLimitDay(rule,day),used));
                }
                RenderLimitProgress(); return true;
            }
            catch (Exception ex) { ShowError("提醒状态保存失败，请重试："+ex.Message); return false; }
        };
        dialog.Closed += (_, _) => { _reminderAudio.Stop(); if (_limitAlert == dialog) { _limitAlert = null; _alertRule = null; } };
        dialog.Show(); dialog.UpdateLayout(); if (rule.Level == ReminderLevel.Severe) dialog.RequestForeground();
    }
    private void CloseLimitAlert(string? key = null)
    {
        if (key is not null && _alertRule?.AppKey != key) return;
        _limitAlert?.DismissSilently(); _limitAlert = null; _alertRule = null;
    }
}
