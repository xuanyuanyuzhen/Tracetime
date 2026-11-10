using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TraceTime.Core;
using TraceTime.Storage;

namespace TraceTime.App;

public partial class MainWindow
{
    private void VerifyLimitOptionsViews(string path, Action<string> save, Action<FrameworkElement,string> saveElement)
    {
        if (!_demo) throw new InvalidOperationException("仅隔离演示允许验证");
        static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
        string Image(string suffix) => Path.ChangeExtension(path,suffix+".png");
        ChangeAppearance(new("light","glass"),false); ShowLimits(this,new());
        var app=_db.RecordedApps().First(); LimitAppPicker.SelectedValue=app.Key; LimitMinutes.Text="60";
        RepeatEntryCheck.IsChecked=false;RuleEmailCheck.IsChecked=true; SaveLimitRule(this,new());
        var rule=_limits.Single(x=>x.AppKey==app.Key);Check(!rule.RepeatOnEntry&&rule.EmailEnabled,"规则选项未保存");
        EditLimitRule(new Button{Tag=app.Key},new());Check(!RepeatEntryCheck.IsChecked!.Value&&RuleEmailCheck.IsChecked==true,"编辑未恢复选项");
        var temp=new TemporaryUseWindow(app.Name,minutes=>StartTemporaryUse(rule,minutes)){ShowActivated=false};temp.Show();temp.UpdateLayout();saveElement((FrameworkElement)temp.Content,Image("temporary-use"));
        temp.MinutesInput.Text="0";temp.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(temp.IsVisible&&temp.Feedback.Text.Length>0,"非法分钟未阻止");
        temp.MinutesInput.Text="15";temp.StartButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var today=DateOnly.FromDateTime(DateTime.Today);var state=_db.LoadLimitDay(rule,today);Check(state.TemporaryUntilForeground.HasValue,"临时额度未保存");
        LimitsPanel.ScrollToBottom();UpdateLayout();save(Image("limit-options"));
        CancelTemporaryUse(new Button{Tag=app.Key},new());Check(_db.LoadLimitDay(rule,today).TemporaryUntilForeground is null,"临时额度未取消");
        int saved=0;var configPath=Path.Combine(Path.GetDirectoryName(path)!,"test-email-settings.json");
        var config=new EmailConfigWindow(new(),settings=>{settings.Save(configPath);saved++;}){ShowActivated=false};config.Show();config.UpdateLayout();saveElement((FrameworkElement)config.Content,Image("email-settings"));
        config.EnabledCheck.IsChecked=true;config.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(saved==0&&config.Feedback.Text.Length>0,"空配置被启用");
        config.HostInput.Text="smtp.example.test";config.PortInput.Text="587";config.FromInput.Text="from@example.test";config.ToInput.Text="to@example.test";config.UserInput.Text="from@example.test";config.PasswordInput.Password="isolated-test-secret";
        config.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(saved==1,"有效邮件配置未保存");
        var restored=EmailSettings.Load(configPath);Check(restored.Password()=="isolated-test-secret"&&!File.ReadAllText(configPath).Contains("isolated-test-secret"),"邮件凭据未加密往返");
        ChangeAppearance(new("dark","glass"),false);
        var compact=new EmailConfigWindow(restored,_=>saved++){ShowActivated=false,Width=480,Height=500};compact.Show();compact.FieldsScroller.ScrollToBottom();compact.UpdateLayout();saveElement((FrameworkElement)compact.Content,Image("email-compact-dark"));
        compact.HostInput.Text="another.example.test";compact.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(saved==1&&compact.IsVisible,"更换服务延用了旧密码");compact.Close();Check(saved==1,"取消配置仍保存");
        var disabled=new EmailConfigWindow(restored,_=>saved++){ShowActivated=false};disabled.Show();disabled.EnabledCheck.IsChecked=false;disabled.HostInput.Text="";disabled.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(saved==2,"关闭邮件不应要求有效连接");
        var reminder=new LimitReminderWindow(app.Name,rule,3600,_icons.Get(app),true,true){ShowActivated=false};reminder.Show();reminder.UpdateLayout();saveElement((FrameworkElement)reminder.Content,Image("temporary-expired"));reminder.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!reminder.IsVisible,"确认按钮不能关闭提醒");
        ChangeAppearance(new("light","glass"),false);ShowLimits(this,new());LimitsPanel.ScrollToTop();UpdateLayout();save(Image("limits-0.6"));
        File.WriteAllText(Path.ChangeExtension(path,"limit-options.txt"),"PASS rule options, temporary duration validation/start/cancel, SMTP validation, DPAPI/reload, service-change credential boundary, cancel/disable, compact dark view and expiry reminder\n");
    }
    internal async Task VerifyEmailPipelineAsync(string path)
    {
        if (!_demo) throw new InvalidOperationException("仅隔离演示允许验证");
        static void Check(bool ok,string message) { if(!ok)throw new Exception(message); }
        var dir=Path.Combine(Path.GetDirectoryName(path)!,"email-pipeline");Directory.CreateDirectory(dir);
        using var db=new UsageDatabase(Path.Combine(dir,"usage.db"));var today=DateOnly.FromDateTime(DateTime.Today);
        var start=ReportBuilder.Bounds(today,TimeZoneInfo.Local).Start;
        db.Save(new([new("mail","mail",new("mail-app","示例应用"),start,start.AddMinutes(10))],[]));
        db.SaveLimit(new("mail-app",1,UsageMetric.Foreground,ReminderLevel.Light,EmailEnabled:true));
        var rule=db.LoadLimits().Single();var usage=db.GetDay(today,TimeZoneInfo.Local).Apps.Single();var state=db.LoadLimitDay(rule,today);
        var notice=EmailNoticePolicy.Due(rule,state,usage).Single();db.QueueEmail(notice);
        var settings=new EmailSettings(true,"smtp.example.test",587,"from@example.test","to@example.test","user").WithPassword("pipeline-test-secret");
        int calls=0;var mode="success";var gate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sender=new FakeReminderSender(async (_,password,_,ct)=>{
            calls++;Check(password=="pipeline-test-secret","错误凭据");
            if(mode=="fail")throw new IOException("pipeline-test-secret private server response");
            if(mode=="wait")await gate.Task.WaitAsync(ct);
        });
        using var service=new ReminderEmailService(db,settings with {Enabled=false},sender);
        await service.DrainAsync(today,true);Check(calls==0,"关闭邮件仍发信");
        service.Update(settings); // settings change cancels old queued notices
        Check(db.ReadEmails(today).Single().Status=="cancelled","更改服务未取消旧队列");db.SetEmailStatus(notice.Id,"queued","");
        await service.DrainAsync(today,false);Check(calls==0,"暂停仍发信");await service.DrainAsync(today,true);
        Check(calls==1&&db.ReadEmails(today).Single().Status=="sent","邮件未完成");await service.DrainAsync(today,true);Check(calls==1,"已发送重复投递");
        db.SetEmailStatus(notice.Id,"queued","");mode="fail";await service.DrainAsync(today,true);await service.DrainAsync(today,true);
        Check(calls==2&&db.ReadEmails(today).Single().Status=="failed"&&!db.ReadEmails(today).Single().Detail.Contains("secret"),"失败重发或泄露诊断");
        db.RetryFailedEmails(today);mode="success";await service.DrainAsync(today,true);Check(calls==3,"手动重试无效");
        db.SetEmailStatus(notice.Id,"queued","");mode="wait";var pending=service.DrainAsync(today,true);await service.DrainAsync(today,true);Check(calls==4&&!pending.IsCompleted,"未保持异步单队列");
        service.Update(settings with {Enabled=false});await pending;Check(db.ReadEmails(today).Single().Status=="failed","中止发送未记录结果未知");
        service.Update(settings);db.SetEmailStatus(notice.Id,"queued","");service.CurrentDay=()=>today.AddDays(1);await service.DrainAsync(today,true);Check(calls==4,"跨日仍发信");
        service.CurrentDay=()=>today;service.CanContinue=()=>false;await service.DrainAsync(today,true);Check(calls==4,"发送期间暂停未生效");
        File.WriteAllText(Path.ChangeExtension(path,"email-flow.txt"),"PASS simulated email disabled/pause, settings cancellation, success dedup, failure privacy/no auto-retry, manual retry, async single-flight, in-flight cancellation and cross-day/pause guard; no SMTP network used\n");
    }
    private sealed class FakeReminderSender(Func<EmailConnection,string,EmailNotice,CancellationToken,Task> send) : IReminderEmailSender
    {
        public Task SendAsync(EmailConnection c,string p,EmailNotice n,CancellationToken ct)=>send(c,p,n,ct);
    }
}
