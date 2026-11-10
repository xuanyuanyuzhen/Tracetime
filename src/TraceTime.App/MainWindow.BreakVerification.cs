using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TraceTime.Core;

namespace TraceTime.App;

public partial class MainWindow
{
    internal async Task VerifyBreakAudioCancellationAsync(string path)
    {
        if (!_demo) throw new InvalidOperationException("仅隔离演示允许验证休息音效。");
        var originalAudio = _breakAudio; var originalSettings = _breakSettings;
        var today = DateOnly.FromDateTime(DateTime.Today); var originalQuiet = _db.IsReminderQuiet(today);
        var output = new FakeAudioOutput();
        using var audio = new ReminderAudioService(Path.Combine(Path.GetDirectoryName(path)!, "break-audio-cancel"), output);
        var wave = ReminderWave.Chime(false); var name = ReminderAudioService.FileNameFor(wave);
        var sound = new SoundChoice("custom", 40, name);
        audio.Save(ReminderSounds.Default.With(ReminderLevel.Light, sound), new System.Collections.Generic.Dictionary<string, byte[]> { [name] = wave });
        try
        {
            CloseBreakAlert(); _breakAudio = audio;
            Action[] cancel = [
                () => { BreakEnabled.IsChecked = false; SaveBreakSettings(this, new()); },
                () => ToggleBreakQuiet(this, new()),
                () => ToggleQuietToday(this, new()),
                () => ToggleManualRest(this, new())
            ];
            foreach (var stop in cancel)
            {
                foreach (var loading in new[] { false, true })
                {
                    _breakSettings = new(true, 30, ReminderLevel.Light); BreakMinutes.Text = "30";
                    _breakClock.Configure(true, TimeSpan.FromMinutes(30), BreakNow, ComputerUseState.Using);
                    _db.SetReminderQuiet(today, false); RenderQuietState();
                    var plays = output.PlayCount;
                    var pending = audio.PlayAsync(sound);
                    if (!loading) await pending;
                    var stops = output.Stops;
                    stop();
                    if (output.Stops <= stops) throw new Exception("关闭/静默/休息未停止轻度提醒音效");
                    await pending;
                    if (output.PlayCount != plays + (loading ? 0 : 1)) throw new Exception("取消后加载中的音效仍然输出");
                    if (_breakAlert is not null) throw new Exception("轻度取消验证意外创建弹窗");
                }
            }
            File.WriteAllText(Path.ChangeExtension(path, "break-audio-cancel.txt"), "PASS custom light reminder: disable/save, rest-only quiet, global quiet and manual rest stop active output and cancel pending asynchronous playback; fake output only, no sound device used.");
        }
        finally
        {
            audio.Stop(); _breakAudio = originalAudio; _breakSettings = originalSettings;
            originalSettings.Save(_breakSettingsPath); _db.SetReminderQuiet(today, originalQuiet); RenderQuietState();
            BreakEnabled.IsChecked = originalSettings.Enabled; BreakMinutes.Text = originalSettings.Minutes.ToString();
            _breakClock.Configure(originalSettings.Enabled, TimeSpan.FromMinutes(originalSettings.Minutes), BreakNow, BreakUseState);
        }
    }

    private void VerifyBreakViews(string path, Action<string> save, Action<FrameworkElement, string> saveElement)
    {
        if (!_demo) throw new InvalidOperationException("仅隔离演示允许验证休息提醒。");
        static void Check(bool value, string text) { if (!value) throw new Exception(text); }
        void Layout() { UpdateLayout(); Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle); }
        string Image(string name) => Path.ChangeExtension(path, name + ".png");
        void Due(ReminderLevel level)
        {
            CloseBreakAlert(); _breakSettings = _breakSettings with { Enabled = true, Minutes = 5, Level = level, IgnoredOn = null };
            _breakClock.Configure(true, TimeSpan.FromMinutes(5), TimeSpan.Zero, ComputerUseState.Using);
            for (var i = 1; i <= 300; i++) _breakClock.Observe(TimeSpan.FromSeconds(i), ComputerUseState.Using);
            _lastReminderSoundAt = 0;
        }
        void Tick(bool available = true, bool quiet = false) => CheckBreakReminder(TimeSpan.FromSeconds(300),
            available ? ComputerUseState.Using : ComputerUseState.Away, available, quiet, false);
        var settings = _breakSettings; var preferences = _viewPreferences; var appearance = _appearance;
        var appSounds = _reminderAudio.Settings; var restSounds = _breakAudio.Settings;
        var report = _db.GetDay(DateOnly.FromDateTime(DateTime.Today), TimeZoneInfo.Local);
        Width = 1160; Height = 860; ChangeAppearance(new("light", "glass"), false);
        SaveViewPreferences(preferences with { SidebarCollapsed = false }); ShowBreaks(this, new()); Layout();
        Check(BreaksPanel.IsVisible && !LimitsPanel.IsVisible && !_breakSettings.Enabled, "休息页面/默认关闭错误");
        save(Image("break-default"));
        BreakMinutes.Text = "4"; BreakEnabled.IsChecked = true; SaveBreakSettings(this, new());
        Check(!_breakSettings.Enabled && BreakFeedback.Text.Contains("5–240"), "非法间隔被保存");
        var preset = VisualChildren(BreaksPanel).OfType<Button>().First(x => x.Tag as string == "45");
        preset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(BreakMinutes.Text == "45", "预设未填入");
        BreakMedium.IsChecked = true; SaveBreakSettings(this, new());
        Check(_breakSettings.Enabled && _breakSettings.Minutes == 45 && BreakReminderSettings.Load(_breakSettingsPath) == _breakSettings, "启用/保存回读失败");
        RenderBreakProgress(); save(Image("break-settings-light"));
        ToggleBreakQuiet(this,new()); Check(_breakSettings.IgnoredOn == DateOnly.FromDateTime(DateTime.Today), "今日静默未保存");
        ToggleBreakQuiet(this,new()); Check(_breakSettings.IgnoredOn is null, "今日静默未恢复");

        Due(ReminderLevel.Medium); _previewOpen = true; Tick(); Check(_breakAlert is null, "预览期间弹出提醒"); _previewOpen = false;
        _historyBusy = true; Tick(); Check(_breakAlert is null, "维护期间弹出提醒"); _historyBusy = false;
        Tick(quiet:true); Check(_breakAlert is null, "今日全部静默仍弹出");
        var blocker = new LimitReminderWindow("演示应用", new("sample", 5, UsageMetric.Foreground, ReminderLevel.Medium), 300, preview:true);
        _limitAlert = blocker; Tick(); Check(_breakAlert is null, "应用额度与休息提醒同时弹出"); _limitAlert = null; blocker.DismissSilently();
        Tick(); var alert = _breakAlert; Check(alert is not null && _breakClock.Pending, "到期未显示弹窗");
        Tick(); Check(ReferenceEquals(alert, _breakAlert), "同一到期重复创建弹窗");
        Tick(false); Check(!alert!.IsVisible, "会话不可用仍显示弹窗"); Tick(); Check(alert.IsVisible, "会话恢复未显示待处理弹窗");
        saveElement((FrameworkElement)alert.Content, Image("break-reminder-light"));
        alert.SnoozeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(_breakAlert is null && _breakClock.NextReminder > _breakClock.Used, "稍后提醒未结束弹窗");

        Due(ReminderLevel.Severe); Tick(); Layout();
        ChangeAppearance(new("dark", "glass"), false); saveElement((FrameworkElement)_breakAlert!.Content, Image("break-reminder-dark"));
        _breakAlert.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(_breakClock.ManualRest && _breakAlert is null, "开始休息按钮失败");
        _breakClock.Observe(BreakNow + TimeSpan.FromMinutes(5), ComputerUseState.Using);
        Check(_breakClock.Used == TimeSpan.Zero, "主动休息满5分钟未归零");
        ToggleManualRest(this,new()); Check(!_breakClock.ManualRest, "继续使用按钮失败");
        Due(ReminderLevel.Medium); Tick(); _breakAlert!.IgnoreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(BreakReminderSettings.Load(_breakSettingsPath).IgnoredOn == DateOnly.FromDateTime(DateTime.Today), "弹窗今天不再提醒未持久化");
        Due(ReminderLevel.Light); Tick(); Check(_breakAlert is null && _breakClock.NextReminder == TimeSpan.FromMinutes(10), "轻度通知没有正确延后");

        var wave = ReminderWave.Chime(false); var name = ReminderAudioService.FileNameFor(wave);
        _breakAudio.Save(restSounds.With(ReminderLevel.Medium,new("custom",40,name)),new System.Collections.Generic.Dictionary<string,byte[]> { [name]=wave });
        Check(File.Exists(_breakAudio.FileFor(_breakAudio.Settings.Medium)) && _reminderAudio.Settings == appSounds, "休息音效未独立保存或改动额度音效");
        using (var reloaded = new ReminderAudioService(Path.Combine(Path.GetDirectoryName(_db.FilePath)!,"break-reminder")))
            Check(reloaded.Settings.Medium.Kind=="custom" && reloaded.Settings.Medium.Volume==40,"自定义音效未恢复");
        var preview = PresentBreakAlert(ReminderLevel.Medium,true,false); var usedBefore = _breakClock.Used;
        preview.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Check(_breakClock.Used==usedBefore&&!_breakClock.ManualRest,"效果预览改动计时");
        _breakAudio.Save(restSounds,new System.Collections.Generic.Dictionary<string,byte[]>());

        BreakMinutes.Text="45"; BreakMedium.IsChecked=true; SaveBreakSettings(this,new());
        Width=900; Height=620; SaveViewPreferences(_viewPreferences with { SidebarCollapsed=true }); BreaksPanel.ScrollToTop(); Layout();
        save(Image("break-compact-dark"));
        SaveBreakButton.BringIntoView(); Layout();
        var saveY = SaveBreakButton.TranslatePoint(new(),BreaksPanel).Y;
        Check(saveY>=0 && saveY+SaveBreakButton.ActualHeight<=BreaksPanel.ViewportHeight+1,"紧凑布局无法访问保存按钮");
        BreakRestButton.BringIntoView(); Layout(); save(Image("break-progress-compact"));
        Check(ResourceUsage.TranslatePoint(new(),(FrameworkElement)Content).Y+ResourceUsage.ActualHeight<=((FrameworkElement)Content).ActualHeight,"新增入口挤出资源栏");
        _breakSettings=settings; settings.Save(_breakSettingsPath); _breakClock.Configure(settings.Enabled,TimeSpan.FromMinutes(settings.Minutes),BreakNow,BreakUseState);
        BreakEnabled.IsChecked=settings.Enabled; BreakMinutes.Text=settings.Minutes.ToString(); BreakMedium.IsChecked=true;
        CloseBreakAlert();CloseBreakPreview();SaveViewPreferences(preferences);ChangeAppearance(appearance,false);Width=1160;Height=860;ShowOverview(this,new());
        Check(_db.GetDay(report.Date,TimeZoneInfo.Local).Foreground==report.Foreground,"休息操作修改使用记录");
        File.WriteAllText(Path.ChangeExtension(path,"break-reminders.txt"),"PASS default off, presets/custom validation/persistence, continuous threshold, preview/maintenance/application-alert/quiet blockers, popup dedup, session hide/resume, rest/snooze/today-ignore buttons, light/medium/severe paths without native sound or focus, independent custom WAV/volume/reload, preview isolation, six navigation entries and compact reachability. No lock/sleep/time changes or real user records.");
    }
}
