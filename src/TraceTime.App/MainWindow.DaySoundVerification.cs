using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TraceTime.Core;

namespace TraceTime.App;

public partial class MainWindow
{
    private void VerifyDaySoundViews(string path,Action<string> save,Action<FrameworkElement,string> saveElement)
    {
        if(!_demo)throw new InvalidOperationException("仅演示允许验证。");void Check(bool value,string error){if(!value)throw new Exception(error);}
        string Image(string suffix)=>Path.ChangeExtension(path,suffix+".png");
        void Chart(string suffix)
        {
            OpenDayDistribution(this,new());
            Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            UpdateLayout();
            var point=Timeline.TranslatePoint(new Point(),OverviewPanel);
            Check(point.Y>=0 && point.Y+Timeline.ActualHeight<=OverviewPanel.ViewportHeight,"图表未完整进入视口，不能以此截图验收");
            save(Image(suffix));
        }
        ShowOverview(this,new());Today(this,new());TimelineExpander.IsExpanded=true;_activityHour=-1;_selectedApp=null;RenderTimeline();
        ActivityBar.IsChecked=true;Chart("day-bars");
        Check(_dayActivity!.Whole.Foreground==_report!.Foreground&&_dayActivity.Whole.Active==_report.WithoutIdle,"整天图表与每日双时长不一致");
        ActivityLine.IsChecked=true;Chart("day-timeline");ActivityRing.IsChecked=true;Chart("day-ring");
        ActivityPeriod.SelectedValue=10;Check(_activityHour==10&&PeriodApps.Items.Count>0,"时段选择未更新应用统计");Chart("day-hour");
        ActivityPeriod.SelectedValue=0;Check(PeriodApps.Items.Count==0&&DetailsList.Items.Count>0,"空档时段未显示");
        Width=900;Height=680;ActivityRing.IsChecked=true;Chart("day-compact");Width=1160;Height=860;
        ChangeAppearance(new("dark","glass"),false);ActivityBar.IsChecked=true;_activityHour=-1;RenderTimeline();Chart("day-dark");
        // Deterministic synthetic now: today remainder is future, not unrecorded.
        var (start,end)=ReportBuilder.Bounds(_report.Date,TimeZoneInfo.Local);RenderTimeline(start.AddHours(12));var future=_dayActivity!;ActivityRing.IsChecked=true;Chart("day-future");
        Check(future.Whole.Future==end-start.AddHours(12),"未来被计作未记录");
        RenderTimeline();ActivityBar.IsChecked=true;
        ShowLimits(this,new());var before=_db.LoadLimits().ToArray();var soundDir=Path.Combine(Path.GetDirectoryName(path)!,"sound-preview");
        var output=new FakeAudioOutput();using var audio=new ReminderAudioService(soundDir,output);int previews=0;
        var dialog=new ReminderEffectsWindow(audio,ReminderLevel.Light,(level,sound,wave)=>{previews++;return Task.CompletedTask;},()=>{});dialog.Show();dialog.UpdateLayout();saveElement((FrameworkElement)dialog.Content,Image("sound-settings"));
        Check(!VisualChildren(dialog.SoundPicker).OfType<TextBlock>().Any(x=>x.Text.Contains("SoundOption")),"音效列表显示了内部对象");
        var custom=ReminderWave.Chime(false);dialog.Import(custom);dialog.VolumeSlider.Value=30;dialog.PreviewButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(previews==1,"未触发效果预览");
        dialog.MediumTab.IsChecked=true;dialog.SoundPicker.SelectedValue="clear";dialog.VolumeSlider.Value=40;
        dialog.SevereTab.IsChecked=true;dialog.SoundPicker.SelectedValue="gentle";dialog.Height=540;dialog.Width=550;dialog.EffectsScroller.ScrollToBottom();dialog.UpdateLayout();saveElement((FrameworkElement)dialog.Content,Image("sound-compact"));
        dialog.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(audio.Settings.Light.Kind=="custom"&&audio.Settings.Medium.Kind=="clear"&&audio.Settings.Severe.Kind=="gentle","三档声音未分别保存");
        using var restored=new ReminderAudioService(soundDir,new FakeAudioOutput());Check(File.Exists(restored.FileFor(restored.Settings.Light)),"自定义声音未复制");
        var beforeJson=File.ReadAllText(restored.SettingsPath);var cancel=new ReminderEffectsWindow(restored,ReminderLevel.Light,(_,_,_)=>Task.CompletedTask,()=>{});cancel.Show();cancel.SoundPicker.SelectedValue="silent";cancel.Close();Check(File.ReadAllText(restored.SettingsPath)==beforeJson,"取消音效设置仍然保存");
        foreach(var level in new[]{ReminderLevel.Medium,ReminderLevel.Severe})
        {
            var preview=new LimitReminderWindow("示例应用",new("preview",60,UsageMetric.Foreground,level),3600,null,true){ShowActivated=false};preview.Show();preview.UpdateLayout();saveElement((FrameworkElement)preview.Content,Image("preview-"+level.ToString().ToLowerInvariant()));preview.SnoozeButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check(!preview.IsVisible,"预览按钮不能关闭");
        }
        Check(before.SequenceEqual(_db.LoadLimits()),"预览修改真实规则");
        var today=DateOnly.FromDateTime(DateTime.Today);var states=before.Select(x=>_db.LoadLimitDay(x,today)).ToArray();
        foreach(var level in Enum.GetValues<ReminderLevel>())
        {
            PreviewReminderAsync(level,new("silent"),null,false).GetAwaiter().GetResult();
            _previewAlert?.ConfirmButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));CloseReminderPreview();
        }
        Check(before.SequenceEqual(_db.LoadLimits())&&states.SequenceEqual(before.Select(x=>_db.LoadLimitDay(x,today))),"三档真实预览回调修改规则或今日状态");
        ChangeAppearance(new("light","glass"),false);ShowOverview(this,new());
        File.WriteAllText(Path.ChangeExtension(path,"day-sound.txt"),"PASS chart variants, hour selection, per-app totals, unrecorded/future slices, preview dialog actions, per-level custom sound copy/persistence/cancel. No actual sound or foreground stealing tested.");
    }
    internal async Task VerifyAudioPipelineAsync(string path)
    {
        if(!_demo)throw new InvalidOperationException();
        var output=new FakeAudioOutput();using var service=new ReminderAudioService(Path.Combine(Path.GetDirectoryName(path)!,"audio-pipeline"),output);
        var bytes=ReminderWave.Chime(true);var custom=new SoundChoice("custom",25,ReminderAudioService.FileNameFor(bytes));
        service.Save(ReminderSounds.Default.With(ReminderLevel.Light,custom),new Dictionary<string,byte[]>{{custom.FileName,bytes}});
        await service.PlayAsync(custom);if(output.PlayCount!=1||!output.Last!.SequenceEqual(ReminderWave.SetVolume(bytes,25)))throw new Exception("音频实际输出未使用自定义或音量");
        await service.PlayAsync(new("silent"));if(output.PlayCount!=1)throw new Exception("静音仍播放");
        await service.PlayAsync(new("system"));if(output.SystemCount!=1)throw new Exception("系统音效未路由");
        var pending=service.PlayAsync(new("gentle"));service.Stop();await pending;if(output.PlayCount!=1)throw new Exception("停止后异步声音继续播放");
        File.WriteAllText(Path.ChangeExtension(path,"audio-flow.txt"),"PASS fake audio output: custom PCM/volume, system and silent routing, cancellation before async load completes. No actual device playback.");
    }
    private sealed class FakeAudioOutput:IReminderAudioOutput
    {public int PlayCount, SystemCount, Stops;public byte[]? Last;public void Play(byte[] wave){PlayCount++;Last=wave;}public void SystemSound()=>SystemCount++;public void Stop()=>Stops++;public void Dispose(){} }
}
