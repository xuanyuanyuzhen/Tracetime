using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using TraceTime.Core;

namespace TraceTime.App;

internal interface IReminderAudioOutput : IDisposable { void Play(byte[] wave); void SystemSound(); void Stop(); }
internal sealed class WindowsReminderAudio : IReminderAudioOutput
{
    private SoundPlayer? _player;private MemoryStream? _stream;
    public void Play(byte[] wave){Stop();_stream=new MemoryStream(wave,false);_player=new SoundPlayer(_stream);_player.Load();_player.Play();}
    public void SystemSound(){Stop();SystemSounds.Exclamation.Play();}
    public void Stop(){_player?.Stop();_player?.Dispose();_stream?.Dispose();_player=null;_stream=null;}
    public void Dispose()=>Stop();
}
internal sealed class ReminderAudioService : IDisposable
{
    private readonly string _directory;
    private readonly IReminderAudioOutput _output;
    private int _generation;
    internal string SettingsPath {get;}
    internal ReminderSounds Settings {get;private set;}
    internal ReminderAudioService(string dataDirectory,IReminderAudioOutput? output=null)
    {
        _directory=Path.Combine(dataDirectory,"sounds");SettingsPath=Path.Combine(dataDirectory,"reminder-sounds.json");Settings=ReminderSounds.Load(SettingsPath);_output=output??new WindowsReminderAudio();
    }
    internal static string FileNameFor(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes))+".wav";
    internal string FileFor(SoundChoice choice)
    {
        var file=choice.FileName;
        if(file.Length!=68||!file.EndsWith(".wav",StringComparison.OrdinalIgnoreCase)||file[..64].Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException("自定义音效文件无效，请重新选择。");
        return Path.Combine(_directory,file);
    }
    internal void Save(ReminderSounds settings,IReadOnlyDictionary<string,byte[]> imports)
    {
        settings=new(settings.Light.Normalize(),settings.Medium.Normalize(),settings.Severe.Normalize());
        Directory.CreateDirectory(_directory);
        foreach(var level in Enum.GetValues<ReminderLevel>())
        {
            var choice=settings.For(level);if(choice.Kind!="custom")continue;var path=FileFor(choice);
            if(imports.TryGetValue(choice.FileName,out var bytes))
            {
                ReminderWave.Validate(bytes);if(FileNameFor(bytes)!=choice.FileName)throw new ArgumentException("音效校验失败。");
                File.WriteAllBytes(path+".tmp",bytes);File.Move(path+".tmp",path,true);
            }
            else ReminderWave.Validate(ReadBounded(path));
        }
        settings.Save(SettingsPath);Settings=settings;
        var keep=Enum.GetValues<ReminderLevel>().Select(x=>settings.For(x)).Where(x=>x.Kind=="custom").Select(x=>x.FileName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var path in Directory.EnumerateFiles(_directory,"*.wav"))
        {
            var file=Path.GetFileName(path);
            if(file.Length==68&&file[..64].All(Uri.IsHexDigit)&&!keep.Contains(file)) {try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
        }
    }
    private static byte[] ReadBounded(string path)
    {
        using var stream=File.OpenRead(path);if(stream.Length>ReminderWave.MaxBytes)throw new ArgumentException("音效超过 2 MB。");
        using var output=new MemoryStream();stream.CopyTo(output);return output.ToArray();
    }
    internal async Task PlayAsync(SoundChoice choice,byte[]? preview=null)
    {
        Stop();var generation=_generation;choice=choice.Normalize();
        if(choice.Kind=="silent")return;
        if(choice.Kind=="system"){_output.SystemSound();return;}
        if(choice.Volume==0)return;
        var data=await Task.Run(()=>ReminderWave.SetVolume(choice.Kind switch {"gentle"=>ReminderWave.Chime(false),"clear"=>ReminderWave.Chime(true),"custom"=>preview??ReadBounded(FileFor(choice)),_=>throw new ArgumentException("音效类型无效。")},choice.Volume));
        if(generation!=_generation)return;_output.Play(data);
    }
    internal void Stop(){_generation++;_output.Stop();}
    public void Dispose(){Stop();_output.Dispose();}
}
