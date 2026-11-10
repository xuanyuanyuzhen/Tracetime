using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using TraceTime.Core;

namespace TraceTime.App;

public partial class MainWindow
{
    private ReminderEmailService _emailService = null!;
    private string _emailSettingsPath = "";
    private void InitializeEmailReminders()
    {
        _emailSettingsPath = Path.Combine(Path.GetDirectoryName(_db.FilePath)!, "email-settings.json");
        _emailService = new(_db, EmailSettings.Load(_emailSettingsPath));
        _emailService.CanContinue = () => !_demo && _tracker?.CanPresentReminder == true && !_db.IsReminderQuiet(DateOnly.FromDateTime(DateTime.Today));
        _emailService.Changed += RenderEmailStatus;
    }
    private bool ObserveLimitVisit()
    {
        if (_tracker is null) return false;
        return _limitVisits.Observe(_tracker.CurrentApp.Key, _tracker.CanPresentReminder,
            string.Equals(_tracker.CurrentApp.ExecutablePath, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase));
    }
    private static string LimitStateText(UsageLimit rule, LimitDayState state, double seconds, double foreground)
    {
        if (!rule.Enabled) return "规则已暂停";
        if (state.Dismissed) return "今天不再提醒（含邮件），明天重新计算";
        if (LimitPolicy.TemporaryActive(state, foreground)) return $"临时使用剩余 {FormatTime(TimeSpan.FromSeconds(state.TemporaryUntilForeground!.Value - foreground))} · 按前台停留累计";
        if (state.Pending) return "等待确认提醒";
        if (LimitPolicy.TemporaryExpired(state, foreground) && !state.TemporaryNotified) return "临时使用已到期，进入应用后提醒";
        if (seconds < state.NextSeconds) return $"每日额度剩余 {FormatTime(TimeSpan.FromSeconds(state.NextSeconds-seconds))}";
        return state.Acknowledged ? (rule.RepeatOnEntry ? "本次已提醒，再次进入应用时提醒" : "今日额度已提醒") : "已到额度，进入应用后提醒";
    }
    private void OpenTemporaryUse(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key || _limits.FirstOrDefault(x=>x.AppKey==key) is not { Enabled: true } rule) return;
        var window = new TemporaryUseWindow(_knownLimitApps.GetValueOrDefault(key)?.Name ?? "已记录应用", minutes => StartTemporaryUse(rule, minutes), _viewPreferences.TemporaryMinutes, minutes => SaveViewPreferences(_viewPreferences with { TemporaryMinutes = minutes })) { Owner = this };
        window.ShowDialog();
    }
    private void StartTemporaryUse(UsageLimit rule, int minutes)
    {
        _tracker?.Flush();
        if (_tracker?.Error is { } error) throw new InvalidOperationException(error);
        var today = DateOnly.FromDateTime(DateTime.Today); var report = _db.GetDay(today,TimeZoneInfo.Local);
        var foreground = LimitPolicy.Seconds(report.Apps.FirstOrDefault(x=>x.App.Key==rule.AppKey),UsageMetric.Foreground);
        _db.SaveLimitDay(LimitPolicy.StartTemporary(_db.LoadLimitDay(rule,today),foreground,minutes,Guid.NewGuid().ToString("N")));
        CloseLimitAlert(rule.AppKey); RenderLimitProgress(); LimitFeedback.Text = $"已开启 {minutes} 分钟临时使用，切到该应用后开始累计。";
    }
    private void CancelTemporaryUse(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string key || _limits.FirstOrDefault(x=>x.AppKey==key) is not { } rule) return;
        try { _db.SaveLimitDay(LimitPolicy.CancelTemporary(_db.LoadLimitDay(rule,DateOnly.FromDateTime(DateTime.Today)))); CloseLimitAlert(key); RenderLimitProgress(); }
        catch (Exception ex) { LimitFeedback.Text = "取消失败："+ex.Message; }
    }
    private void OpenEmailSettings(object sender, RoutedEventArgs e)
    {
        new EmailConfigWindow(_emailService.Settings, settings => {
            settings.Save(_emailSettingsPath); _emailService.Update(settings); RenderLimitProgress();
            LimitFeedback.Text = settings.Enabled ? "已保存邮件设置。已开启邮件的应用达到额度后会自动发送；到达过额度的应用也会检查。" : "邮件预警已关闭。";
        }) { Owner = this }.ShowDialog();
    }
    private void QueueDueEmails(DayReport report)
    {
        if (!_emailService.Settings.Enabled || _db.IsReminderQuiet(report.Date)) return;
        foreach (var rule in _limits.Where(x=>x.Enabled && x.EmailEnabled))
        {
            var usage = report.Apps.FirstOrDefault(x=>x.App.Key==rule.AppKey); if (usage is null) continue;
            foreach (var notice in EmailNoticePolicy.Due(rule,_db.LoadLimitDay(rule,report.Date),usage)) _db.QueueEmail(notice);
        }
    }
    private async Task DrainEmailQueueAsync(DateOnly today)
    {
        try { await _emailService.DrainAsync(today, !_demo && _tracker?.CanPresentReminder == true); }
        catch { if (_ready) EmailSummary.Text = "邮件状态暂时无法保存，请检查数据目录后重试。"; }
    }
    private async void RetryEmails(object sender, RoutedEventArgs e)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (!_emailService.Settings.Enabled) { EmailSummary.Text = "请先开启邮件预警。"; return; }
            _db.RetryFailedEmails(today); await DrainEmailQueueAsync(today); RenderEmailStatus();
        }
        catch { EmailSummary.Text = "重试失败，请检查数据目录是否可写。"; }
    }
    private void RenderEmailStatus()
    {
        if (!_ready) return;
        var notices = _db.ReadEmails(DateOnly.FromDateTime(DateTime.Today));
        EmailSummary.Text = _emailService.Settings.Enabled ? $"已启用 · 今日发信服务接收 {notices.Count(x=>x.Status=="sent")} 封，失败 {notices.Count(x=>x.Status=="failed")} 封" : "邮件预警未启用 · 本地提醒正常可用";
        EmailAttempts.ItemsSource = notices.Take(12).Select(x=>new { Text = $"{x.Name} · {(x.Kind=="temporary"?"临时到期":"每日额度")} · {x.Detail}" }).ToArray();
        RetryEmailButton.IsEnabled = _emailService.Settings.Enabled && notices.Any(x=>x.Status=="failed");
    }
}
