using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TraceTime.Core;
using TraceTime.Storage;

namespace TraceTime.App;

// Owned by the UI dispatcher: database access stays on that thread; SMTP awaits without blocking it.
internal sealed class ReminderEmailService : IDisposable
{
    private readonly UsageDatabase _db;
    private readonly IReminderEmailSender _sender;
    private CancellationTokenSource? _attempt;
    private string? _activeKey;
    private bool _busy, _disposed;
    internal EmailSettings Settings { get; private set; }
    internal Func<bool> CanContinue { get; set; } = () => true;
    internal Func<DateOnly> CurrentDay { get; set; } = () => DateOnly.FromDateTime(DateTime.Today);
    internal event Action? Changed;
    internal ReminderEmailService(UsageDatabase db, EmailSettings settings, IReminderEmailSender? sender = null)
    {
        _db = db; Settings = settings; _sender = sender ?? new SmtpReminderEmailSender();
        _db.RecoverEmailAttempts();
    }
    internal void Update(EmailSettings settings)
    {
        _db.CancelQueuedEmails(); Settings = settings; _attempt?.Cancel();
    }
    internal void CancelRule(string key)
    {
        _db.CancelRuleEmail(key);
        if (_activeKey == key) _attempt?.Cancel();
    }
    internal void CancelActive() => _attempt?.Cancel();
    internal async Task DrainAsync(DateOnly today, bool canSend)
    {
        if (_busy || _disposed || !canSend || !Settings.Enabled) return;
        _busy = true;
        try
        {
            _db.PruneEmails(today);
            foreach (var notice in _db.ReadEmails(today).Where(x => x.Status == "queued").Reverse())
            {
                if (_disposed || !Settings.Enabled || !CanContinue() || CurrentDay() != today) break;
                var rule = _db.LoadLimits().FirstOrDefault(x => x.AppKey == notice.AppKey);
                if (rule is null || !rule.Enabled || !rule.EmailEnabled || rule.Revision != notice.Revision
                    || _db.LoadLimitDay(rule, today).Dismissed)
                { _db.SetEmailStatus(notice.Id, "cancelled", "规则已更改或今天已忽略"); continue; }
                // Re-read: a settings/rule change while awaiting the previous notice may cancel this one.
                if (_db.ReadEmails(today).FirstOrDefault(x => x.Id == notice.Id)?.Status != "queued") continue;
                _activeKey = notice.AppKey;
                using var attempt = new CancellationTokenSource(TimeSpan.FromSeconds(22)); _attempt = attempt;
                _db.SetEmailStatus(notice.Id, "sending", "正在发送"); Changed?.Invoke();
                try
                {
                    var settings = Settings; settings.Connection.Validate();
                    await _sender.SendAsync(settings.Connection, settings.Password(), notice, attempt.Token);
                    if (!_disposed) _db.SetEmailStatus(notice.Id, "sent", "发信服务已接收；实际到达以收件箱为准");
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Never persist provider responses: they can include account or credential material.
                    if (!_disposed) _db.SetEmailStatus(notice.Id, "failed", ex is OperationCanceledException
                        ? "发送已中止或超时，结果未知。请检查收件箱后再重试。"
                        : "发送失败。请检查网络、STARTTLS端口及授权码，再手动重试。");
                }
                finally { _attempt = null; _activeKey = null; }
                if (_disposed) break;
                Changed?.Invoke();
            }
        }
        finally { _busy = false; }
    }
    public void Dispose() { _disposed = true; _attempt?.Cancel(); Changed = null; }
}
