using System;
using System.Windows;

namespace TraceTime.App;

public partial class EmailConfigWindow : Window
{
    private readonly EmailSettings _original;
    private readonly Action<EmailSettings> _save;
    internal EmailConfigWindow(EmailSettings settings, Action<EmailSettings> save)
    {
        InitializeComponent(); _original = settings; _save = save;
        EnabledCheck.IsChecked = settings.Enabled; HostInput.Text = settings.Host; PortInput.Text = settings.Port.ToString();
        FromInput.Text = settings.From; ToInput.Text = settings.To; UserInput.Text = settings.UserName;
        if (settings.ProtectedPassword.Length > 0) PasswordHint.Text = "已保存加密凭据。账号未变时留空可沿用；更换发信服务或账号须重新输入。";
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
    }
    private void Save(object sender, RoutedEventArgs e)
    {
        try
        {
            var enabled = EnabledCheck.IsChecked == true;
            var validPort = int.TryParse(PortInput.Text, out var port);
            if (!validPort && enabled) throw new ArgumentException("请输入有效的 SMTP 端口，通常为 587。");
            var draft = new EmailSettings(enabled, HostInput.Text.Trim(), validPort ? port : 587,
                FromInput.Text.Trim(), ToInput.Text.Trim(), UserInput.Text.Trim());
            if (draft.SameAccount(_original)) draft = draft with { ProtectedPassword = _original.ProtectedPassword };
            if (PasswordInput.Password.Length > 0) draft = draft.WithPassword(PasswordInput.Password);
            if (enabled)
            {
                draft.Connection.Validate();
                if (string.IsNullOrWhiteSpace(draft.Password())) throw new ArgumentException("请填写发信服务的授权码或应用密码。");
            }
            _save(draft); PasswordInput.Clear(); Close();
        }
        catch (ArgumentException ex) { Feedback.Text = ex.Message; }
        catch { Feedback.Text = "无法保存或解密凭据。请重新输入授权码，并检查数据目录是否可写。"; }
    }
    private void Cancel(object sender, RoutedEventArgs e) { PasswordInput.Clear(); Close(); }
}
