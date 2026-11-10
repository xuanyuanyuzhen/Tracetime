using TraceTime.Core;

namespace TraceTime.Storage;

public sealed partial class UsageDatabase
{
    private void InitializeEmail() => Execute("""
        CREATE TABLE IF NOT EXISTS email_notices (
            id TEXT PRIMARY KEY, app_key TEXT NOT NULL, day TEXT NOT NULL, revision INTEGER NOT NULL,
            name TEXT NOT NULL, kind TEXT NOT NULL, metric INTEGER NOT NULL, seconds REAL NOT NULL,
            minutes INTEGER NOT NULL, status TEXT NOT NULL, detail TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS email_status ON email_notices(day,status);
        """);
    public void QueueEmail(EmailNotice notice) => Execute("""
        INSERT OR IGNORE INTO email_notices VALUES($id,$key,$day,$revision,$name,$kind,$metric,$seconds,$minutes,'queued','等待发送')
        """, null, ("$id", notice.Id), ("$key", notice.AppKey), ("$day", notice.Day.ToString("yyyy-MM-dd")),
        ("$revision", notice.Revision), ("$name", notice.Name), ("$kind", notice.Kind), ("$metric", (int)notice.Metric),
        ("$seconds", notice.Seconds), ("$minutes", notice.Minutes));
    public IReadOnlyList<EmailNotice> ReadEmails(DateOnly day)
    {
        using var cmd = Command("SELECT id,app_key,revision,name,kind,metric,seconds,minutes,status,detail FROM email_notices WHERE day=$day ORDER BY rowid DESC", null, ("$day", day.ToString("yyyy-MM-dd")));
        using var r = cmd.ExecuteReader(); var list = new List<EmailNotice>();
        while (r.Read()) list.Add(new(r.GetString(0), r.GetString(1), day, r.GetInt32(2), r.GetString(3), r.GetString(4),
            (UsageMetric)r.GetInt32(5), r.GetDouble(6), r.GetInt32(7), r.GetString(8), r.GetString(9)));
        return list;
    }
    public void SetEmailStatus(string id, string status, string detail)
    {
        if (status is not ("queued" or "sending" or "sent" or "failed" or "cancelled")) throw new ArgumentException("无效邮件状态。");
        Execute("UPDATE email_notices SET status=$status,detail=$detail WHERE id=$id", null, ("$status", status), ("$detail", detail), ("$id", id));
    }
    public void RecoverEmailAttempts() => Execute("UPDATE email_notices SET status='failed',detail='上次发送被中断，投递结果未知；请先检查收件箱，再决定重试。' WHERE status='sending'");
    public void RetryFailedEmails(DateOnly day) => Execute("UPDATE email_notices SET status='queued',detail='等待手动重试' WHERE day=$day AND status='failed'", null, ("$day", day.ToString("yyyy-MM-dd")));
    public void CancelRuleEmail(string key) => Execute("UPDATE email_notices SET status='cancelled',detail='规则已更改或提醒已关闭' WHERE app_key=$key AND status IN ('queued','failed')", null, ("$key", key));
    public void CancelQueuedEmails(string detail = "邮件设置已更改或关闭") => Execute("UPDATE email_notices SET status='cancelled',detail=$detail WHERE status IN ('queued','failed')",null,("$detail",detail));
    public void PruneEmails(DateOnly day)
    {
        Execute("DELETE FROM email_notices WHERE day<$day", null, ("$day", day.AddDays(-7).ToString("yyyy-MM-dd")));
        Execute("UPDATE email_notices SET status='cancelled',detail='已跨日，不补发过期提醒' WHERE day<>$day AND status IN ('queued','failed')", null, ("$day", day.ToString("yyyy-MM-dd")));
    }
}
