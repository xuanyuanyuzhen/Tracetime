using System.Text;
using System.Text.Json;

namespace TraceTime.Core;

public sealed record SoundChoice(string Kind = "system", int Volume = 60, string FileName = "")
{
    public SoundChoice Normalize() => new(Kind is "system" or "gentle" or "clear" or "silent" or "custom" ? Kind : "system", Math.Clamp(Volume,0,100), Kind=="custom" ? Path.GetFileName(FileName ?? "") : "");
}
public sealed record ReminderSounds(SoundChoice Light, SoundChoice Medium, SoundChoice Severe)
{
    public static ReminderSounds Default => new(new(), new("silent"), new("silent"));
    public SoundChoice For(ReminderLevel level) => level switch { ReminderLevel.Light => Light, ReminderLevel.Medium => Medium, _ => Severe };
    public ReminderSounds With(ReminderLevel level, SoundChoice value) => level switch { ReminderLevel.Light => this with { Light=value }, ReminderLevel.Medium => this with { Medium=value }, _ => this with { Severe=value } };
    public static ReminderSounds Load(string path)
    {
        try { var value=JsonSerializer.Deserialize<ReminderSounds>(File.ReadAllText(path)); return value is null ? Default : new((value.Light??Default.Light).Normalize(),(value.Medium??Default.Medium).Normalize(),(value.Severe??Default.Severe).Normalize()); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException) { return Default; }
    }
    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(path+".tmp",path,true);
    }
}

public sealed record WaveInfo(int Channels, int SampleRate, int Bits, int DataOffset, int DataSize)
{
    public double Seconds => DataSize / (double)(SampleRate * Channels * (Bits / 8));
}
public static class ReminderWave
{
    public const int MaxBytes = 2 * 1024 * 1024;
    public static WaveInfo Validate(byte[] data)
    {
        if(data.Length < 44 || data.Length > MaxBytes) throw new ArgumentException("请选择不超过 2 MB、10 秒的 PCM WAV 音效。");
        using var reader=new BinaryReader(new MemoryStream(data));
        string Four()=>Encoding.ASCII.GetString(reader.ReadBytes(4));
        if(Four()!="RIFF") throw new ArgumentException("音效须为 WAV 文件。");
        var riffSize=reader.ReadUInt32(); if(riffSize+8L!=data.Length || Four()!="WAVE") throw new ArgumentException("WAV 文件不完整。");
        int channels=0,rate=0,bits=0,offset=0,size=0; bool pcm=false,formatSeen=false,dataSeen=false;
        while(reader.BaseStream.Position+8<=data.Length)
        {
            var tag=Four();var length=reader.ReadUInt32();var next=reader.BaseStream.Position+length;
            if(next>data.Length) throw new ArgumentException("WAV 文件内容不完整。");
            if(tag=="fmt " && length>=16)
            {
                if(formatSeen)throw new ArgumentException("请选择只包含一种 PCM 格式的 WAV 音效。");
                formatSeen=true;
                pcm=reader.ReadUInt16()==1;channels=reader.ReadUInt16();rate=reader.ReadInt32();var byteRate=reader.ReadInt32();var align=reader.ReadUInt16();bits=reader.ReadUInt16();
                if(channels is <1 or >2 || rate is <8000 or >96000 || bits is not(8 or 16) || align!=channels*(bits/8) || byteRate!=rate*align) throw new ArgumentException("请使用 8/16 位、单声道或双声道 PCM WAV。");
            }
            else if(tag=="data")
            {
                if(dataSeen)throw new ArgumentException("请选择只包含一个音频数据段的 WAV 音效。");
                dataSeen=true;offset=(int)reader.BaseStream.Position;size=(int)length;
            }
            reader.BaseStream.Position=next+(length%2);
        }
        if(!pcm || channels==0 || rate==0 || bits==0 || size<=0 || size%(channels*(bits/8))!=0) throw new ArgumentException("请使用标准 PCM WAV 音效。");
        var info=new WaveInfo(channels,rate,bits,offset,size);
        if(info.Seconds>10) throw new ArgumentException("提示音最长 10 秒，请先裁剪音频。"); return info;
    }
    public static byte[] SetVolume(byte[] source,int volume)
    {
        var info=Validate(source);var data=(byte[])source.Clone();var scale=Math.Clamp(volume,0,100)/100d;
        for(int i=info.DataOffset;i<info.DataOffset+info.DataSize;i+=info.Bits/8)
        {
            if(info.Bits==8) data[i]=(byte)(128+(data[i]-128)*scale);
            else {short sample=(short)((short)(data[i]|(data[i+1]<<8))*scale);data[i]=(byte)sample;data[i+1]=(byte)(sample>>8);}
        }
        return data;
    }
    public static byte[] Chime(bool clear)
    {
        const int rate=22050;var seconds=clear?1.1:.65;var count=(int)(rate*seconds);
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+count*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(count*2);
        for(int i=0;i<count;i++){var t=i/(double)rate;var local=clear?t%.4:t;var envelope=Math.Min(1,local/.015)*Math.Exp(-local*8);var tone=Math.Sin(2*Math.PI*(clear?740:523.25)*t);writer.Write((short)(tone*envelope*9000));}return stream.ToArray();
    }
}
