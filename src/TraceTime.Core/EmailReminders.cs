using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;

namespace TraceTime.Core;

public sealed record EmailConnection(string Host, int Port, string From, string To, string UserName)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host) || Host.Length > 253 || Uri.CheckHostName(Host) == UriHostNameType.Unknown)
            throw new ArgumentException("请输入 SMTP 主机名，例如 smtp.example.com（不含 https://）。");
        if (Port is < 1 or > 65535 || Port == 465)
            throw new ArgumentException("请选择支持 STARTTLS 的端口，通常为 587；此版本不支持 465 隐式 TLS。");
        if (!Address(From) || !Address(To)) throw new ArgumentException("请输入完整的发件邮箱和一个收件邮箱。");
        if (string.IsNullOrWhiteSpace(UserName) || UserName.Length > 320 || UserName.Any(char.IsControl))
            throw new ArgumentException("请输入 SMTP 登录账号。");
    }
    private static bool Address(string value) => !string.IsNullOrEmpty(value) && value.Length <= 320 && !value.Any(char.IsControl)
        && MailAddress.TryCreate(value, out var parsed) && parsed.Address == value && value.Contains('@');
}

public sealed record EmailNotice(string Id, string AppKey, DateOnly Day, int Revision, string Name, string Kind,
    UsageMetric Metric, double Seconds, int Minutes, string Status = "queued", string Detail = "")
{
    public string Subject => Kind == "temporary" ? "TraceTime · 临时使用已到期" : "TraceTime · 每日应用额度已达到";
    public string Body => $"{Day:yyyy-MM-dd}\n{Name}\n" + (Kind == "temporary" ? "本次临时使用额度已用完。" : $"已达到每日 {Minutes} 分钟的额度。")
        + $"\n今日{(Metric == UsageMetric.Foreground ? "前台停留" : "扣除空闲后")}累计 {TimeSpan.FromSeconds(Seconds).TotalMinutes.ToString("0.0", CultureInfo.InvariantCulture)} 分钟。"
        + "\n\n这是你在 TraceTime 中开启的可选预警，可在应用时长限制中关闭。空闲不代表未工作。";
}

public static class EmailNoticePolicy
{
    public static IEnumerable<EmailNotice> Due(UsageLimit rule, LimitDayState state, AppUsage usage)
    {
        if (!rule.Enabled || !rule.EmailEnabled || state.Dismissed) yield break;
        var seconds = LimitPolicy.Seconds(usage, rule.Metric);
        if (seconds >= rule.Minutes * 60d) yield return Notice("daily", "daily");
        if (LimitPolicy.TemporaryExpired(state, usage.Foreground.TotalSeconds) && state.TemporaryToken is { } token)
            yield return Notice("temporary", token);
        EmailNotice Notice(string kind, string token)
        {
            var identity = $"{rule.AppKey}\n{state.Date:yyyy-MM-dd}\n{rule.Revision}\n{kind}\n{token}";
            return new(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))), rule.AppKey, state.Date,
                rule.Revision, usage.App.Name, kind, rule.Metric, seconds, rule.Minutes);
        }
    }
}

public interface IReminderEmailSender
{
    Task SendAsync(EmailConnection connection, string password, EmailNotice notice, CancellationToken cancellationToken);
}

public sealed class SmtpReminderEmailSender : IReminderEmailSender
{
    public async Task SendAsync(EmailConnection connection, string password, EmailNotice notice, CancellationToken cancellationToken)
    {
        connection.Validate();
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("请输入 SMTP 授权码或应用密码。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var message = new MailMessage(connection.From, connection.To, notice.Subject, notice.Body) {
            SubjectEncoding = Encoding.UTF8, BodyEncoding = Encoding.UTF8, IsBodyHtml = false };
        using var client = new SmtpClient(connection.Host, connection.Port) {
            EnableSsl = true, UseDefaultCredentials = false, Credentials = new NetworkCredential(connection.UserName, password),
            DeliveryMethod = SmtpDeliveryMethod.Network };
        // EnableSsl requires STARTTLS. A server without it fails; credentials never fall back to plaintext.
        await client.SendMailAsync(message, timeout.Token).ConfigureAwait(false);
    }
}
