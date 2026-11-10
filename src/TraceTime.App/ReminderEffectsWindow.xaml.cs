using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TraceTime.Core;

namespace TraceTime.App;

public partial class ReminderEffectsWindow : Window
{
    private readonly ReminderAudioService _audio;
    private readonly Func<ReminderLevel,SoundChoice,byte[]?,Task> _preview;
    private readonly Action _closePreview;
    private ReminderSounds _draft;private ReminderLevel _level;private bool _ready;
    private readonly Dictionary<string,byte[]> _imports=new();
    public sealed record SoundOption(string Kind,string Name);
    internal ReminderEffectsWindow(ReminderAudioService audio,ReminderLevel level,Func<ReminderLevel,SoundChoice,byte[]?,Task> preview,Action closePreview)
    {
        _audio=audio;_draft=audio.Settings;_level=level;_preview=preview;_closePreview=closePreview;
        InitializeComponent();MaxHeight=SystemParameters.WorkArea.Height;
        SoundPicker.ItemsSource=new[]{new SoundOption("system","Windows 系统提示音"),new SoundOption("gentle","柔和单音"),new SoundOption("clear","清脆提醒"),new SoundOption("silent","静音（保留视觉提醒）"),new SoundOption("custom","自定义 WAV")};
        LightTab.IsChecked=level==ReminderLevel.Light;MediumTab.IsChecked=level==ReminderLevel.Medium;SevereTab.IsChecked=level==ReminderLevel.Severe;_ready=true;Sync();
        SourceInitialized+=(_,_)=>ThemeManager.ApplyTitleBar(this);Closing+=(_,_)=>{_audio.Stop();_closePreview();};
    }
    private void Sync()
    {
        _ready=false;var choice=_draft.For(_level);SoundPicker.SelectedValue=choice.Kind;VolumeSlider.Value=choice.Volume;_ready=true;
        EffectDescription.Text=_level switch{ReminderLevel.Light=>"轻度 · 系统通知与提示音，不主动切换前台。",ReminderLevel.Medium=>"中度 · 置顶提醒，需点击按钮结束；不主动请求焦点。",_=>"严重 · 唤起 TraceTime 并置顶提醒，需点击按钮结束。"};UpdateHints();
    }
    private void UpdateHints()
    {
        var choice=_draft.For(_level);VolumeLabel.Text=$"{choice.Volume}%";
        VolumeSlider.IsEnabled=choice.Kind is not("silent" or "system");VolumeHint.Text=choice.Kind=="system"?"系统提示音跟随Windows系统音量，不使用此滑块。":choice.Kind=="silent"?"此档不主动播放应用音效，仍保留视觉提醒。":"试听与实际提醒共用此音量；系统主音量仍然生效。";
        CustomStatus.Text=string.IsNullOrEmpty(choice.FileName)?"尚未选择自定义音效":"已选择自定义短音效";
    }
    private void LevelChanged(object sender,RoutedEventArgs e){if(!_ready||sender is not RadioButton {Tag:string level})return;_audio.Stop();_closePreview();_level=Enum.Parse<ReminderLevel>(level);Sync();}
    private void SoundChanged(object sender,SelectionChangedEventArgs e){if(!_ready)return;_audio.Stop();_draft=_draft.With(_level,_draft.For(_level) with {Kind=SoundPicker.SelectedValue as string??"silent"});UpdateHints();}
    private void VolumeChanged(object sender,RoutedPropertyChangedEventArgs<double> e){if(!_ready)return;_audio.Stop();_draft=_draft.With(_level,_draft.For(_level) with {Volume=(int)VolumeSlider.Value});UpdateHints();}
    private async void ChooseFile(object sender,RoutedEventArgs e)
    {
        var dialog=new OpenFileDialog{Filter="PCM WAV 音效 (*.wav)|*.wav",CheckFileExists=true,Multiselect=false};if(dialog.ShowDialog(this)!=true)return;
        ChooseFileButton.IsEnabled=false;
        try
        {
            var bytes=await Task.Run(()=>{using var file=File.OpenRead(dialog.FileName);if(file.Length>ReminderWave.MaxBytes)throw new ArgumentException("请选择不超过2MB的音效。");using var stream=new MemoryStream();file.CopyTo(stream);return stream.ToArray();});
            if(!IsVisible)return;Import(bytes);Feedback.Text="已载入音效。可先试听，点击保存后才使用。";
        }
        catch(Exception ex) when(ex is ArgumentException or IOException or UnauthorizedAccessException){Feedback.Text="导入失败："+ex.Message;}
        finally{ChooseFileButton.IsEnabled=true;}
    }
    internal void Import(byte[] bytes)
    {
        ReminderWave.Validate(bytes);var name=ReminderAudioService.FileNameFor(bytes);_imports[name]=bytes;_draft=_draft.With(_level,_draft.For(_level) with {Kind="custom",FileName=name});
        var used=Enum.GetValues<ReminderLevel>().Select(x=>_draft.For(x).FileName).ToHashSet();foreach(var key in new List<string>(_imports.Keys))if(!used.Contains(key))_imports.Remove(key);Sync();
    }
    private byte[]? Pending()=>_imports.GetValueOrDefault(_draft.For(_level).FileName);
    private async void Listen(object sender,RoutedEventArgs e)
    {
        try{await _audio.PlayAsync(_draft.For(_level),Pending());Feedback.Text=_draft.For(_level).Kind=="silent"?"此档为静音。":"试听已开始；可以随时停止。";}
        catch{Feedback.Text="无法播放音效，请重新选择有效WAV，或改用内置音效。";}
    }
    private void Stop(object sender,RoutedEventArgs e){_audio.Stop();Feedback.Text="已停止试听。";}
    private async void Preview(object sender,RoutedEventArgs e)
    {
        try{await _preview(_level,_draft.For(_level),Pending());Feedback.Text="已预览；额度和今日提醒状态保持不变。";}
        catch{Feedback.Text="音效预览失败；视觉提醒仍可关闭，请检查自定义文件。";}
    }
    private void Save(object sender,RoutedEventArgs e){try{_audio.Save(_draft,_imports);Close();}catch(Exception ex){Feedback.Text="保存失败："+ex.Message;}}
    private void Reset(object sender,RoutedEventArgs e){_draft=ReminderSounds.Default;_audio.Stop();Sync();Feedback.Text="已恢复默认，点击保存后生效。";}
    private void Cancel(object sender,RoutedEventArgs e)=>Close();
}
