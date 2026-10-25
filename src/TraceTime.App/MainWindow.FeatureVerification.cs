using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using TraceTime.Core;

namespace TraceTime.App;

public partial class MainWindow
{
    // Invoked only by explicit --demo --capture in an isolated directory. No real AI or sound.
    private void VerifyFeatureViews(string path, Action<string> save, Action<FrameworkElement, string> saveElement)
    {
        if (!_demo) throw new InvalidOperationException("功能截图只允许隔离演示模式。");
        string Image(string suffix) => Path.ChangeExtension(path, suffix + ".png");
        static void Check(bool condition, string error) { if (!condition) throw new Exception(error); }
        Today(this,new()); ForegroundMetric.IsChecked = true;
        ChangeAppearance(new("light", "glass"), false); ShowLimits(this,new());
        Check(LimitAppPicker.Items.Count > 0, "已记录应用列表为空");
        var selected=(AppIdentity)LimitAppPicker.SelectedItem;
        LimitMinutes.Text="0"; SaveLimitRule(this,new()); Check(LimitFeedback.Text.Contains("1–1440"),"额度校验缺失");
        LimitMinutes.Text="60"; LevelMedium.IsChecked=true; SaveLimitRule(this,new());
        var rule=_limits.Single(x=>x.AppKey==selected.Key); var day=DateOnly.FromDateTime(DateTime.Today);
        Check(rule.Minutes==60 && rule.Level==ReminderLevel.Medium,"额度控件未保存");
        save(Image("limits"));
        Check(!VisualChildren(LimitAppPicker).OfType<TextBlock>().Any(x=>x.Text.Contains("AppIdentity")),"应用选择框泄露内部对象表示");
        LimitAppPicker.IsDropDownOpen=true; UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        if(LimitAppPicker.Template.FindName("PART_Popup",LimitAppPicker) is System.Windows.Controls.Primitives.Popup {Child:FrameworkElement list}) saveElement(list,Image("limits-app-picker"));
        LimitAppPicker.IsDropDownOpen=false;
        ChangeAppearance(new("dark","glass"),false); save(Image("limits-dark"));
        LimitsPanel.ScrollToBottom(); save(Image("limits-progress")); LimitsPanel.ScrollToTop();
        var state=_db.LoadLimitDay(rule,day); _db.SaveLimitDay(LimitPolicy.Begin(state));
        PresentLimitAlert(selected.Name,rule,day,3700);
        var reminder=_limitAlert!;
        Check(reminder.Topmost && !reminder.ShowActivated,"中度提醒激活策略错误");
        reminder.Close(); Check(reminder.IsVisible,"中度提醒可未经确认关闭");
        reminder.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        Check(reminder.SnoozeButton.ActualHeight>0 && reminder.ConfirmButton.ActualHeight>0,"提醒按钮未布局");
        saveElement((FrameworkElement)reminder.Content,Image("reminder-medium"));
        reminder.SnoozeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(_limitAlert is null && _db.LoadLimitDay(rule,day).NextSeconds>rule.Minutes*60,"延后状态未保存");
        // Severe window is inspected without triggering focus theft during automated QA.
        var severe=new LimitReminderWindow(selected.Name,rule with {Level=ReminderLevel.Severe},3700) {ShowActivated=false};
        severe.Show(); severe.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        saveElement((FrameworkElement)severe.Content,Image("reminder-severe"));
        severe.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(!severe.IsVisible,"严重提醒按钮未关闭");
        IgnoreLimitToday(new Button {Tag=selected.Key},new()); Check(_db.LoadLimitDay(rule,day).Dismissed,"今日忽略未保存");
        ToggleLimitRule(new Button {Tag=selected.Key},new()); Check(!_limits.Single().Enabled,"暂停规则失败");
        ToggleLimitRule(new Button {Tag=selected.Key},new()); Check(_limits.Single().Enabled,"恢复规则失败");
        Width=900; Height=680; save(Image("limits-compact")); Width=1160; Height=860;
        ShowAnalysis(this,new()); TrendTab.IsChecked=true; save(Image("analysis"));
        Analysis30.IsChecked=true; save(Image("analysis-30"));
        RankingTab.IsChecked=true; save(Image("analysis-ranking"));
        SummaryTab.IsChecked=true; save(Image("analysis-summary"));
        Check(_analysis is not null && _analysis.Days.Count==30 && LocalSummary.Text.Contains("空闲"),"本地分析视图失败");
        AiEndpoint.Text="http://localhost:11434/v1/chat/completions"; AiModel.Text="your-model"; AiKey.Password="synthetic-qa-key";
        SaveAiConfig(this,new()); var saved=File.ReadAllText(_aiSettingsPath);
        Check(!saved.Contains("synthetic-qa-key") && AiSettings.Load(_aiSettingsPath).Key()=="synthetic-qa-key","密钥未保护或不能恢复");
        ForgetAiKey(this,new()); Check(AiSettings.Load(_aiSettingsPath).ProtectedKey=="","清除密钥失败");
        PrepareAiPreview(this,new()); Check(_prepared is not null && !AiSend.IsEnabled,"AI预览未准备或未经确认可发送");
        AiConsent.IsChecked=true; Check(AiSend.IsEnabled,"AI同意控件未生效");
        AiModel.Text="changed-model"; Check(_prepared is null && !AiSend.IsEnabled && AiConsent.IsChecked!=true,"修改配置没有使同意失效");
        AiModel.Text="your-model"; PrepareAiPreview(this,new());
        AiConfigExpander.IsExpanded=true; AnalysisPanel.ScrollToBottom(); save(Image("ai-preview"));
        Width=900; Height=680; save(Image("ai-compact")); Width=1160; Height=860;
        ChangeAppearance(new("light","glass"),false); AnalysisPanel.ScrollToTop(); TrendTab.IsChecked=true; save(Image("analysis-light"));
        _db.DeleteLimit(selected.Key); _limits=_db.LoadLimits();
        // A fresh, separate window verifies first-use messaging without deleting seeded records.
        using (var emptyDb=new TraceTime.Storage.UsageDatabase(Path.Combine(Path.GetDirectoryName(path)!,"empty-features","usage.db")))
        {
            var empty=new MainWindow(emptyDb,null,true); empty.Show(); empty.ShowLimits(this,new()); empty.UpdateLayout();
            Check(empty.NoRecordedApps.Visibility==Visibility.Visible && !empty.SaveLimitButton.IsEnabled,"无记录应用引导缺失");
            saveElement((FrameworkElement)empty.Content,Image("limits-empty"));
            empty.DisposeTray(); empty.Hide();
        }
        // Serious reminders use ShowLimits too: it must load historical apps after a fresh launch.
        using (var historyDb=new TraceTime.Storage.UsageDatabase(Path.Combine(Path.GetDirectoryName(path)!,"history-features","usage.db")))
        {
            var yesterday=DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
            var (start,_)=ReportBuilder.Bounds(yesterday,TimeZoneInfo.Local);
            var app=new AppIdentity("historical-only","历史应用名称");
            historyDb.Save(new([new("historical","historical",app,start.AddHours(9),start.AddHours(10))],[]));
            historyDb.SaveLimit(new(app.Key,30,UsageMetric.Foreground,ReminderLevel.Light));
            var today=yesterday.AddDays(1); var (todayStart,_)=ReportBuilder.Bounds(today,TimeZoneInfo.Local);
            var currentApp=new AppIdentity("today-due","今日到额应用");
            historyDb.Save(new([new("today-due","today-due",currentApp,todayStart.AddHours(9),todayStart.AddHours(10))],[]));
            historyDb.SaveLimit(new(currentApp.Key,1,UsageMetric.Foreground,ReminderLevel.Severe));
            var restored=new MainWindow(historyDb,null,true); restored.Show(); restored.ShowLimits(this,new()); restored.UpdateLayout();
            var due=LimitDispatch.Next(historyDb.LoadLimits(),historyDb.GetDay(today,TimeZoneInfo.Local),rule=>historyDb.LoadLimitDay(rule,today),true);
            Check(due?.Rule.AppKey==currentApp.Key && due.Rule.Level==ReminderLevel.Severe,"严重规则未到额度");
            Check(restored.LimitAppPicker.Items.Count==2 && restored._knownLimitApps.ContainsKey(app.Key),"首次唤起额度页未加载历史应用");
            var row=restored.LimitRulesList.Items.Cast<object>().Single(x=>(string?)x.GetType().GetProperty("AppKey")?.GetValue(x)==app.Key);
            Check((string?)row.GetType().GetProperty("Name")?.GetValue(row)==app.Name,"历史应用被错误标记为已删除");
            restored.DisposeTray(); restored.Hide();
        }
        ShowOverview(this,new());
        File.WriteAllText(Path.ChangeExtension(path,"features.txt"),"PASS rule editor/validation, pending reminder acknowledgment, snooze, ignore, enable/disable, empty recorded-app guidance, historical-app names on first limit-page entry, 7/30-day views, DPAPI key save/load/clear, AI preview and invalidation. No live AI request or focus-stealing QA performed.");
    }
}
