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

    private void ShowLimits(object sender, RoutedEventArgs e) { SetPage("limits"); RefreshRecordedApps(sender, e); RenderLimitProgress(); }
    private void RefreshRecordedApps(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = (LimitAppPicker.SelectedItem as AppVisual)?.Key;
            _tracker?.Flush(); var apps = _db.RecordedApps();
            _knownLimitApps = apps.ToDictionary(x => x.Key);
            var choices=apps.Select(_icons.Get).ToArray();
            LimitAppPicker.ItemsSource = choices; LimitAppPicker.SelectedItem = choices.FirstOrDefault(x => x.Key == selected) ?? choices.FirstOrDefault();
            NoRecordedApps.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            SaveLimitButton.IsEnabled = apps.Count > 0;
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
            _db.SaveLimit(new(app.Key, minutes, LimitWithoutIdle.IsChecked == true ? UsageMetric.WithoutIdle : UsageMetric.Foreground, LevelSevere.IsChecked == true ? ReminderLevel.Severe : LevelMedium.IsChecked == true ? ReminderLevel.Medium : ReminderLevel.Light));
            _limits = _db.LoadLimits(); CloseLimitAlert(app.Key); RenderLimitProgress();
            LimitFeedback.Text = $"已保存 {app.Name} 的每日额度。保存后重新判断今天累计时长，已到额度会触发提醒。";
        }
        catch (Exception ex) { LimitFeedback.Text = "保存失败：" + ex.Message; }
    }
    private void ClearLimitEditor(object sender, RoutedEventArgs e) { LimitMinutes.Text = "60"; LimitForeground.IsChecked = LevelLight.IsChecked = true; LimitFeedback.Text = ""; }
    private void EditLimitRule(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key || _limits.FirstOrDefault(x => x.AppKey == key) is not { } rule) return;
        RefreshRecordedApps(sender, e); LimitAppPicker.SelectedValue = key;
        LimitMinutes.Text = rule.Minutes.ToString(); LimitForeground.IsChecked = rule.Metric == UsageMetric.Foreground; LimitWithoutIdle.IsChecked = rule.Metric == UsageMetric.WithoutIdle;
        LevelLight.IsChecked = rule.Level == ReminderLevel.Light; LevelMedium.IsChecked = rule.Level == ReminderLevel.Medium; LevelSevere.IsChecked = rule.Level == ReminderLevel.Severe;
        LimitsPanel.ScrollToTop(); LimitFeedback.Text = "编辑后点击保存。重新保存会重置今天的提醒状态。";
    }
    private void ToggleLimitRule(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key || _limits.FirstOrDefault(x => x.AppKey == key) is not { } rule) return;
        try { _db.SetLimitEnabled(key, !rule.Enabled); _limits = _db.LoadLimits(); CloseLimitAlert(key); RenderLimitProgress(); }
        catch (Exception ex) { LimitFeedback.Text = ex.Message; }
    }
    private void RemoveLimitRule(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key) return;
        try { _db.DeleteLimit(key); _limits = _db.LoadLimits(); CloseLimitAlert(key); RenderLimitProgress(); LimitFeedback.Text = "已删除额度规则，使用记录保留。"; }
        catch (Exception ex) { LimitFeedback.Text = ex.Message; }
    }
    private void IgnoreLimitToday(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key || _limits.FirstOrDefault(x => x.AppKey == key) is not { } rule) return;
        try { _db.SaveLimitDay(LimitPolicy.Dismiss(_db.LoadLimitDay(rule, DateOnly.FromDateTime(DateTime.Today)))); CloseLimitAlert(key); RenderLimitProgress(); }
        catch (Exception ex) { LimitFeedback.Text = ex.Message; }
    }
    private void RefreshLimitProgress(object sender, RoutedEventArgs e) { _tracker?.Flush(); RenderLimitProgress(); }
    private void RenderLimitProgress()
    {
        if (!_ready) return;
        try
        {
            var day = DateOnly.FromDateTime(DateTime.Today); var report = _db.GetDay(day, TimeZoneInfo.Local);
            foreach(var app in report.Apps) _knownLimitApps[app.App.Key] = app.App;
            LimitRulesList.ItemsSource = _limits.Select(rule => {
                var seconds = LimitPolicy.Seconds(report.Apps.FirstOrDefault(x => x.App.Key == rule.AppKey), rule.Metric);
                var state = _db.LoadLimitDay(rule, day); var fraction = Math.Clamp(seconds / (rule.Minutes * 60d), 0, 1);
                return new {
                    Visual = _icons.Get(_knownLimitApps.GetValueOrDefault(rule.AppKey) ?? new(rule.AppKey,"已删除使用记录的应用")),
                    rule.AppKey, Name = _knownLimitApps.GetValueOrDefault(rule.AppKey)?.Name ?? "已删除使用记录的应用", LevelText = LevelName(rule.Level), ToggleText = rule.Enabled ? "暂停规则" : "启用规则",
                    ProgressText = $"{MetricName(rule.Metric)}  {FormatTime(TimeSpan.FromSeconds(seconds))} / {rule.Minutes} 分钟 · {seconds / (rule.Minutes * 60d):P0}",
                    Fraction = new GridLength(fraction, GridUnitType.Star), Rest = new GridLength(1 - fraction, GridUnitType.Star),
                    StateText = !rule.Enabled ? "规则已暂停" : state.Dismissed ? "今天不再提醒，明天重新计算" : state.Pending ? "等待确认提醒" : state.NextSeconds > rule.Minutes * 60 ? $"已延后，累计到 {FormatTime(TimeSpan.FromSeconds(state.NextSeconds))} 再提醒" : seconds >= state.NextSeconds ? "已到额度，正在等待提醒" : $"剩余 {FormatTime(TimeSpan.FromSeconds(state.NextSeconds - seconds))}"
                };
            }).ToArray();
            LimitsEmpty.Visibility = _limits.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { LimitFeedback.Text = "读取进度失败：" + ex.Message; }
    }
    private static string LevelName(ReminderLevel level) => level switch { ReminderLevel.Light => "轻度", ReminderLevel.Medium => "中度", _ => "严重" };
    private static string MetricName(UsageMetric metric) => metric == UsageMetric.Foreground ? "前台停留" : "扣除空闲后";

    private void CheckLimits()
    {
        if (_demo || _tracker is null) return;
        if (!_tracker.CanPresentReminder) { _limitAlert?.Hide(); return; }
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (_limitAlert is not null)
            {
                if (_alertDay != today) CloseLimitAlert();
                else if (_alertRule?.Level == ReminderLevel.Severe || !_limits.Any(x=>x.Enabled && x.Level==ReminderLevel.Severe))
                { if (!_limitAlert.IsVisible) _limitAlert.Show(); return; }
            }
            if (!_limits.Any(x => x.Enabled)) return;
            if (_prunedDay != today) { _db.PruneLimitDays(today); _prunedDay = today; }
            _tracker.Flush(); if (_tracker.Error is not null) return;
            var report = _db.GetDay(today, TimeZoneInfo.Local);
            var next = LimitDispatch.Next(_limits,report,rule=>_db.LoadLimitDay(rule,today),_tracker.CanPresentReminder);
            if (_limitAlert is not null && next?.Rule.Level!=ReminderLevel.Severe)
            { if (!_limitAlert.IsVisible) _limitAlert.Show(); return; }
            if (next is not null)
            {
                if (_limitAlert is not null) CloseLimitAlert();
                var rule=next.Rule; var state=next.State; var app=next.Usage; var seconds=next.Seconds;
                _db.SaveLimitDay(LimitPolicy.Begin(state));
                if (rule.Level == ReminderLevel.Light)
                {
                    _tray?.ShowBalloonTip(6000, "TraceTime · 每日额度提醒", $"{app.App.Name} 今日{MetricName(rule.Metric)}达到 {rule.Minutes} 分钟。可以休息一下。", System.Windows.Forms.ToolTipIcon.Info);
                    try { System.Media.SystemSounds.Exclamation.Play(); } catch { }
                    _db.SaveLimitDay(LimitPolicy.Dismiss(state));
                    return;
                }
                PresentLimitAlert(app.App.Name, rule, today, seconds);
            }
        }
        catch (Exception ex) { ShowError("应用额度检查失败：" + ex.Message); }
    }
    private void PresentLimitAlert(string name, UsageLimit rule, DateOnly day, double seconds)
    {
        _alertRule = rule; _alertDay = day;
        if (rule.Level == ReminderLevel.Severe) { Reveal(); ShowLimits(this,new()); }
        var dialog = new LimitReminderWindow(name, rule, seconds,_icons.Get(_knownLimitApps.GetValueOrDefault(rule.AppKey) ?? new(rule.AppKey,name))); _limitAlert = dialog;
        dialog.Accept = snooze => {
            try
            {
                var current = _limits.FirstOrDefault(x => x.AppKey == rule.AppKey);
                if (DateOnly.FromDateTime(DateTime.Today) == day && current?.Revision == rule.Revision)
                {
                    _tracker?.Flush(); var report = _db.GetDay(day, TimeZoneInfo.Local);
                    var used = LimitPolicy.Seconds(report.Apps.FirstOrDefault(x => x.App.Key == rule.AppKey), rule.Metric);
                    var state = _db.LoadLimitDay(rule, day);
                    _db.SaveLimitDay(snooze ? LimitPolicy.Snooze(state, used) : LimitPolicy.Dismiss(state));
                }
                RenderLimitProgress(); return true;
            }
            catch (Exception ex) { ShowError("提醒状态保存失败，请重试：" + ex.Message); return false; }
        };
        dialog.Closed += (_, _) => { if (_limitAlert == dialog) { _limitAlert = null; _alertRule = null; } };
        dialog.Show(); dialog.UpdateLayout(); if (rule.Level == ReminderLevel.Severe) dialog.RequestForeground();
    }
    private void CloseLimitAlert(string? key = null)
    {
        if (key is not null && _alertRule?.AppKey != key) return;
        _limitAlert?.DismissSilently(); _limitAlert = null; _alertRule = null;
    }
}
