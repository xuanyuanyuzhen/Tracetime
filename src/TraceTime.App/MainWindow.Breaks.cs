using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using TraceTime.Core;

namespace TraceTime.App;

public partial class MainWindow
{
    private BreakReminderSettings _breakSettings = new();
    private readonly BreakReminderClock _breakClock = new();
    private ReminderAudioService _breakAudio = null!;
    private string _breakSettingsPath = "";
    private LimitReminderWindow? _breakAlert, _breakPreview;
    private bool _checkingBreak;
    private bool _allRemindersQuiet;
    private DateOnly _quietStateDay;
    private long _lastReminderSoundAt;
    private static TimeSpan BreakNow => Stopwatch.GetElapsedTime(0);
    private ComputerUseState BreakUseState => _tracker?.ComputerUseState ?? ComputerUseState.Paused;
    private const string EyeRestText = "让视线离开屏幕，看看远处，轻轻眨眼。也可以起身活动一下，给双眼和身体留一点休息时间。";

    private void InitializeBreakReminders()
    {
        var folder = Path.GetDirectoryName(_db.FilePath)!;
        _breakSettingsPath = Path.Combine(folder, "break-reminder.json");
        _breakSettings = BreakReminderSettings.Load(_breakSettingsPath);
        _breakAudio = new ReminderAudioService(Path.Combine(folder, "break-reminder"));
        _breakClock.Configure(_breakSettings.Enabled, TimeSpan.FromMinutes(_breakSettings.Minutes), BreakNow, BreakUseState);
        BreakEnabled.IsChecked = _breakSettings.Enabled; BreakMinutes.Text = _breakSettings.Minutes.ToString();
        BreakLight.IsChecked = _breakSettings.Level == ReminderLevel.Light;
        BreakMedium.IsChecked = _breakSettings.Level == ReminderLevel.Medium;
        BreakSevere.IsChecked = _breakSettings.Level == ReminderLevel.Severe;
    }

    private void ShowBreaks(object sender, RoutedEventArgs e) { SetPage("breaks"); RenderBreakProgress(); }
    private void PickBreakMinutes(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string minutes) BreakMinutes.Text = minutes;
    }
    private void SaveBreakSettings(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(BreakMinutes.Text, out var minutes) || minutes is < 5 or > 240)
        { BreakFeedback.Text = "请输入5–240之间的整数分钟，然后重新保存。"; return; }
        try
        {
            var settings = new BreakReminderSettings(BreakEnabled.IsChecked == true, minutes,
                BreakSevere.IsChecked == true ? ReminderLevel.Severe : BreakLight.IsChecked == true ? ReminderLevel.Light : ReminderLevel.Medium);
            settings.Save(_breakSettingsPath); _breakSettings = settings;
            CloseBreakAlert(); _breakClock.Configure(settings.Enabled, TimeSpan.FromMinutes(minutes), BreakNow, BreakUseState);
            BreakFeedback.Text = settings.Enabled ? $"已开启，每连续使用 {minutes} 分钟提醒。本轮从现在开始计时。" : "休息提醒已关闭，应用使用记录继续保留。";
            RenderBreakProgress();
        }
        catch (Exception ex) { BreakFeedback.Text = "保存失败，当前设置未改变：" + ex.Message; }
    }
    private void ToggleManualRest(object sender, RoutedEventArgs e)
    {
        if (!_breakSettings.Enabled) return;
        if (_breakClock.ManualRest) _breakClock.Resume(BreakNow, BreakUseState);
        else _breakClock.BeginRest(BreakNow, BreakUseState);
        CloseBreakAlert(); RenderBreakProgress();
    }
    private void ToggleBreakQuiet(object sender, RoutedEventArgs e)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var settings = _breakSettings with { IgnoredOn = _breakSettings.IgnoredOn == today ? null : today };
            settings.Save(_breakSettingsPath); _breakSettings = settings;
            CloseBreakAlert(); _breakClock.Snooze(); RenderBreakProgress();
        }
        catch (Exception ex) { BreakFeedback.Text = "今日提醒状态未保存：" + ex.Message; }
    }

    private void TickBreakReminder()
    {
        if (!_ready || _checkingBreak || _demo || !_breakSettings.Enabled) return;
        _checkingBreak = true;
        try
        {
            var quiet = _breakSettings.IgnoredOn == DateOnly.FromDateTime(DateTime.Today);
            CheckBreakReminder(BreakNow, BreakUseState, _tracker?.CanPresentReminder == true,
                quiet || (_allRemindersQuiet && _quietStateDay == DateOnly.FromDateTime(DateTime.Today)));
            if (CanRefreshViews && BreaksPanel.IsVisible) RenderBreakProgress();
        }
        catch (Exception ex) { if (_breakAlert is null) _breakClock.ReleasePresentation(); BreakFeedback.Text = "休息提醒暂时无法更新，将重试：" + ex.Message; }
        finally { _checkingBreak = false; }
    }

    private void CheckBreakReminder(TimeSpan now, ComputerUseState state, bool sessionAvailable, bool quiet, bool nativeEffects = true)
    {
            _breakClock.Observe(now, state);
            if (_breakAlert is not null && !_breakClock.Pending) CloseBreakAlert();
            var canPresent = sessionAvailable && !_historyBusy && !_previewOpen && !_breakClock.ManualRest;
            if (!canPresent) { _breakAlert?.Hide(); _breakAudio.Stop(); }
            else if (quiet) CloseBreakAlert();
            else if (_breakAlert is not null) { if (!_breakAlert.IsVisible) _breakAlert.Show(); }
            else if (_limitAlert is null && Stopwatch.GetElapsedTime(_lastReminderSoundAt) >= TimeSpan.FromSeconds(6) && _breakClock.IsDue(true))
            {
                _breakClock.Presented();
                if (_breakSettings.Level == ReminderLevel.Light)
                {
                    if (nativeEffects) _tray?.ShowBalloonTip(6000, "TraceTime · 该休息一下了", $"本轮连续使用已达 {_breakSettings.Minutes} 分钟。" + EyeRestText, System.Windows.Forms.ToolTipIcon.Info);
                    _breakClock.NotificationShown(); // Light notifications repeat at the user's chosen interval.
                }
                else PresentBreakAlert(_breakSettings.Level, false, nativeEffects);
                if (nativeEffects) PlayBreakSound();
            }
    }

    private void RenderBreakProgress()
    {
        if (!_ready) return;
        BreakRestButton.IsEnabled = BreakQuietButton.IsEnabled = _breakSettings.Enabled;
        BreakRestButton.Content = _breakClock.ManualRest ? "结束休息，继续使用" : "现在开始休息";
        BreakQuietButton.Content = _breakSettings.IgnoredOn == DateOnly.FromDateTime(DateTime.Today) ? "恢复今天的休息提醒" : "今天不再提醒休息";
        BreakCurrent.Text = !_breakSettings.Enabled ? "休息提醒未开启" : _breakClock.ManualRest
            ? $"正在休息 · {ChartDetails.Duration(_breakClock.RestElapsed)}"
            : $"本轮连续使用 · {ChartDetails.Duration(_breakClock.Used)}";
        BreakProgressText.Text = !_breakSettings.Enabled ? "选择适合自己的间隔与强度，勾选启用并保存。"
            : _demo ? "演示模式不累计真实使用、不自动发出提醒。"
            : _breakClock.ManualRest ? (_breakClock.RestElapsed >= BreakReminderClock.RestDuration ? "已休息满5分钟，本轮已归零。准备好后点击继续使用。" : "休息满5分钟后归零；提前结束会保留本轮已用时长。")
            : _breakSettings.IgnoredOn == DateOnly.FromDateTime(DateTime.Today) ? "今天的休息提醒已静默，明天自动恢复；本轮时长继续累计。"
            : _allRemindersQuiet && _quietStateDay == DateOnly.FromDateTime(DateTime.Today) ? "今日全部静默已开启，休息提醒也暂停；可在应用时长限制页恢复。"
            : BreakUseState != ComputerUseState.Using ? "目前暂停累计。已知锁屏或休眠满5分钟后会重置本轮。"
            : _breakClock.Used >= _breakClock.NextReminder ? "已到提醒时间，待当前提醒或预览结束后呈现。"
            : $"再使用 {ChartDetails.Duration(_breakClock.NextReminder - _breakClock.Used)} 后提醒 · {LevelName(_breakSettings.Level)}";
    }

    private LimitReminderWindow PresentBreakAlert(ReminderLevel level, bool preview, bool nativeEffects = true)
    {
        var message = new ReminderMessage("TraceTime · 休息提醒", "该让眼睛休息一下了",
            $"本轮连续使用 {FormatTime(_breakClock.Used)} · 设定间隔 {_breakSettings.Minutes} 分钟", EyeRestText,
            "开始休息后暂停本轮累计，满5分钟后归零；使用记录仍正常记录。也可稍后提醒或今天静默。");
        var rule = new UsageLimit("break-reminder", _breakSettings.Minutes, UsageMetric.Foreground, level);
        var dialog = new LimitReminderWindow("休息提醒", rule, _breakClock.Used.TotalSeconds, preview: preview, message: message);
        if (!nativeEffects) dialog.ShowActivated = false;
        if (preview) _breakPreview = dialog;
        else
        {
            _breakAlert = dialog;
            dialog.Acknowledge = () => { _breakClock.BeginRest(BreakNow, BreakUseState); RenderBreakProgress(); return true; };
            dialog.Accept = snooze =>
            {
                try
                {
                    _breakClock.Observe(BreakNow, BreakUseState);
                    if (!snooze) { var settings = _breakSettings with { IgnoredOn = DateOnly.FromDateTime(DateTime.Today) }; settings.Save(_breakSettingsPath); _breakSettings = settings; }
                    _breakClock.Snooze(); RenderBreakProgress(); return true;
                }
                catch (Exception ex) { BreakFeedback.Text = "保存提醒状态失败，请重试：" + ex.Message; return false; }
            };
        }
        dialog.Closed += (_, _) => { _breakAudio.Stop(); if (_breakAlert == dialog) _breakAlert = null; if (_breakPreview == dialog) _breakPreview = null; };
        if (level == ReminderLevel.Severe && nativeEffects && !preview) { Reveal(); ShowBreaks(this, new()); }
        dialog.Show(); if (level == ReminderLevel.Severe && nativeEffects) dialog.RequestForeground();
        return dialog;
    }
    private async void PlayBreakSound()
    {
        _lastReminderSoundAt = Stopwatch.GetTimestamp();
        try { await _breakAudio.PlayAsync(_breakAudio.Settings.For(_breakSettings.Level)); }
        catch { BreakFeedback.Text = "休息提示音无法播放，视觉提醒仍可使用；请重新选择音效。"; }
    }
    private void CloseBreakAlert() { _breakAudio.Stop(); _breakAlert?.DismissSilently(); _breakAlert = null; _breakClock.ReleasePresentation(); }
    private void CloseBreakPreview() { _breakAudio.Stop(); _breakPreview?.DismissSilently(); _breakPreview = null; }
    private async Task PreviewBreakAsync(ReminderLevel level, SoundChoice sound, byte[]? wave, bool nativeEffects = true)
    {
        CloseBreakPreview();
        if (level == ReminderLevel.Light && nativeEffects) _tray?.ShowBalloonTip(5000, "TraceTime · 休息提醒预览", EyeRestText, System.Windows.Forms.ToolTipIcon.Info);
        else PresentBreakAlert(level, true, nativeEffects);
        await _breakAudio.PlayAsync(sound, wave);
    }
    private void OpenBreakEffects(object sender, RoutedEventArgs e)
    {
        if (_effectsWindow is not null) { _effectsWindow.Activate(); return; }
        if (_limitAlert is not null || _breakAlert is not null) { BreakFeedback.Text = "请先处理当前提醒，再预览效果。"; return; }
        var level = BreakSevere.IsChecked == true ? ReminderLevel.Severe : BreakLight.IsChecked == true ? ReminderLevel.Light : ReminderLevel.Medium;
        var dialog = new ReminderEffectsWindow(_breakAudio, level, (l, s, b) => PreviewBreakAsync(l, s, b), CloseBreakPreview) { Owner = this };
        dialog.Title = "休息提醒 · 预览与音效";
        dialog.EffectsScope.Text = "仅设置休息提醒的音效，与应用时长限制互不影响。预览不改变计时、开关或今日提醒状态。";
        _effectsWindow = dialog; _previewOpen = true;
        try { dialog.ShowDialog(); } finally { _effectsWindow = null; _previewOpen = false; CloseBreakPreview(); }
    }

    internal string BreakLoadedState => $"{_breakSettings.Enabled}|{_breakSettings.Minutes}|{_breakSettings.Level}|{_breakSettings.IgnoredOn:yyyy-MM-dd}|{_breakClock.Used.TotalSeconds:0}";
}
