using System;
using System.Threading.Tasks;
using System.Windows;
using TraceTime.Core;

namespace TraceTime.App;

public partial class MainWindow
{
    private readonly ReminderAudioService _reminderAudio;
    private LimitReminderWindow? _previewAlert;
    private ReminderEffectsWindow? _effectsWindow;
    private bool _previewOpen;
    private void OpenReminderEffects(object sender,RoutedEventArgs e)
    {
        if(_effectsWindow is not null){_effectsWindow.Activate();return;}
        if(_limitAlert is not null){_limitAlert.Activate();LimitFeedback.Text="请先处理当前额度提醒，再预览其他效果。";return;}
        var level=LevelSevere.IsChecked==true?ReminderLevel.Severe:LevelMedium.IsChecked==true?ReminderLevel.Medium:ReminderLevel.Light;
        var dialog=new ReminderEffectsWindow(_reminderAudio,level,(l,s,b)=>PreviewReminderAsync(l,s,b),CloseReminderPreview){Owner=this};_effectsWindow=dialog;_previewOpen=true;
        try{dialog.ShowDialog();}finally{_effectsWindow=null;_previewOpen=false;CloseReminderPreview();}
    }
    private async Task PreviewReminderAsync(ReminderLevel level,SoundChoice sound,byte[]? wave,bool nativeEffects=true)
    {
        CloseReminderPreview();
        var visual=LimitAppPicker.SelectedItem as AppVisual;
        var rule=new UsageLimit(visual?.Key??"preview",int.TryParse(LimitMinutes.Text,out var value)?Math.Clamp(value,1,1440):60,LimitWithoutIdle.IsChecked==true?UsageMetric.WithoutIdle:UsageMetric.Foreground,level);
        if(level==ReminderLevel.Light && nativeEffects)
            _tray?.ShowBalloonTip(5000,"TraceTime · 轻度效果预览",$"这是预览：{visual?.Name??"示例应用"} 达到每日额度。不会修改实际记录。",System.Windows.Forms.ToolTipIcon.Info);
        else if(level!=ReminderLevel.Light)
        {
            var alert=new LimitReminderWindow(visual?.Name??"示例应用",rule,rule.Minutes*60,visual,true);_previewAlert=alert;
            if(!nativeEffects)alert.ShowActivated=false;
            alert.Closed+=(_,_)=>{_reminderAudio.Stop();if(_previewAlert==alert)_previewAlert=null;};alert.Show();if(level==ReminderLevel.Severe&&nativeEffects)alert.RequestForeground();
        }
        await _reminderAudio.PlayAsync(sound,wave);
    }
    private void CloseReminderPreview(){_reminderAudio.Stop();_previewAlert?.DismissSilently();_previewAlert=null;}
    private async void PlayLimitSound(ReminderLevel level)
    {
        if(_demo)return;
        try{await _reminderAudio.PlayAsync(_reminderAudio.Settings.For(level));}
        catch{ShowError("自定义提示音无法播放；视觉提醒不受影响。请在提醒预览与音效中重新选择。");}
    }
}
