using TraceTime.Core;

internal static class DaySoundTests
{
    private static readonly DateOnly Day=new(2026,10,25);
    private static readonly DateTimeOffset Start=new(2026,10,25,0,0,0,TimeSpan.Zero);
    internal static readonly (string Name,Action Test)[] All=[
        ("全天状态补齐睡眠和未记录空档，不将未来计作未用",()=>{
            var report=ReportBuilder.Build(Day,TimeZoneInfo.Utc,[new("u","u",new("a","A"),Start.AddHours(9),Start.AddHours(10))],[new("i","u",Start.AddHours(9.5),Start.AddHours(10))],[new("g",Start.AddHours(10),Start.AddHours(11),"休眠")]);
            var a=new DayActivity(report,TimeZoneInfo.Utc,Start.AddHours(12));
            Check(a.Whole.Active.TotalMinutes==30&&a.Whole.Idle.TotalMinutes==30&&a.Whole.Interrupted.TotalHours==1&&a.Whole.Unrecorded.TotalHours==10&&a.Whole.Future.TotalHours==12);
            Check(a.Entries.First().Start==Start&&a.Entries.Last().End==Start.AddDays(1));
            for(int i=1;i<a.Entries.Count;i++)Check(a.Entries[i-1].End==a.Entries[i].Start);
        }),
        ("空白历史日整天未记录，未来日期不算关机",()=>{
            var report=new DayReport(Day,[],[]);
            Check(new DayActivity(report,TimeZoneInfo.Utc,Start.AddDays(1)).Whole.Unrecorded.TotalHours==24);
            var future=new DayActivity(report,TimeZoneInfo.Utc,Start.AddDays(-1));Check(future.Whole.Future.TotalHours==24&&future.Whole.Unrecorded==TimeSpan.Zero);
        }),
        ("分时统计裁剪应用边界，双口径与每日合计一致",()=>{
            var a=new AppIdentity("a","A");var b=new AppIdentity("b","B");
            var report=ReportBuilder.Build(Day,TimeZoneInfo.Utc,[new("a","a",a,Start.AddHours(9.5),Start.AddHours(10.5)),new("b","b",b,Start.AddHours(10.5),Start.AddHours(11))],[new("i","a",Start.AddHours(10),Start.AddHours(10.25))]);
            var day=new DayActivity(report,TimeZoneInfo.Utc,Start.AddDays(1));var hour=day.Hours[10].Slice;
            Check(hour.Apps.Count==2&&hour.Apps.Single(x=>x.App.Key=="a").Foreground.TotalMinutes==30&&hour.Apps.Single(x=>x.App.Key=="a").WithoutIdle.TotalMinutes==15);
            Check(day.Hours.Sum(x=>x.Slice.Foreground.Ticks)==report.Foreground.Ticks&&day.Hours.Sum(x=>x.Slice.Active.Ticks)==report.WithoutIdle.Ticks);
        }),
        ("全天空闲仍有应用前台时长，空档不归属应用",()=>{
            var report=ReportBuilder.Build(Day,TimeZoneInfo.Utc,[new("u","u",new("a","A"),Start.AddHours(2),Start.AddHours(4))],[new("i","u",Start.AddHours(2),Start.AddHours(4))]);
            var day=new DayActivity(report,TimeZoneInfo.Utc,Start.AddDays(1));Check(day.Whole.Apps.Single().Foreground.TotalHours==2&&day.Whole.Active==TimeSpan.Zero&&day.Whole.Unrecorded.TotalHours==22);
            Check(day.Hours[1].Slice.Apps.Count==0);
        }),
        ("相邻同应用原始时间片完整保留且合计不变",()=>{
            var app=new AppIdentity("a","A");
            var rows=Enumerable.Range(0,45).Select(i=>new TimelineEntry(Start.AddHours(9).AddMinutes(i*2),Start.AddHours(9).AddMinutes(i*2+2),app.Key,app.Name,"idle")).ToArray();
            var day=new DayActivity(new(Day,[new(app,TimeSpan.FromMinutes(90),TimeSpan.Zero)],rows),TimeZoneInfo.Utc,Start.AddDays(1));
            Check(day.Entries.Count(x=>x.Kind=="idle")==45&&day.Whole.Idle.TotalMinutes==90&&day.Whole.Foreground.TotalMinutes==90);
        }),
        ("中断与应用重叠时证据优先，不重复累计",()=>{
            var report=ReportBuilder.Build(Day,TimeZoneInfo.Utc,[new("u","u",new("a","A"),Start.AddHours(9),Start.AddHours(10))],[],[new("g",Start.AddHours(9.5),Start.AddHours(11),"中断")]);
            var day=new DayActivity(report,TimeZoneInfo.Utc,Start.AddDays(1));Check(day.Whole.Foreground.TotalHours==1&&day.Whole.Interrupted.TotalHours==1&&day.Entries.Sum(x=>(x.End-x.Start).TotalHours)==24);
        }),
        ("夏令时23和25小时完整分桶，同名小时携带偏移",()=>{
            var zone=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            foreach(var (date,count) in new[]{(new DateOnly(2026,3,8),23),(new DateOnly(2026,11,1),25)})
            {var (_,end)=ReportBuilder.Bounds(date,zone);var day=new DayActivity(new(date,[],[]),zone,end);Check(day.Hours.Count==count&&day.Whole.Unrecorded.TotalHours==count);Check(day.Hours.All(x=>x.Label.Contains('(')));}
        }),
        ("内置WAV有效，音量零保留时长且样本静音",()=>{
            foreach(var clear in new[]{false,true}){var wave=ReminderWave.Chime(clear);var info=ReminderWave.Validate(wave);Check(info.Seconds>0&&info.Seconds<2);var silent=ReminderWave.SetVolume(wave,0);Check(silent.Skip(info.DataOffset).All(b=>b==0));Check(ReminderWave.Validate(silent).DataSize==info.DataSize);}
        }),
        ("WAV拒绝过大、截断、非PCM及过长声音",()=>{
            Throws(()=>ReminderWave.Validate(new byte[ReminderWave.MaxBytes+1]));var wave=ReminderWave.Chime(false);Throws(()=>ReminderWave.Validate(wave[..^1]));wave[20]=3;Throws(()=>ReminderWave.Validate(wave));
            var longWave=ReminderWave.Chime(true);var samples=ReminderWave.Validate(longWave);Array.Resize(ref longWave,44+22050*2*11);BitConverter.GetBytes(longWave.Length-8).CopyTo(longWave,4);BitConverter.GetBytes(longWave.Length-44).CopyTo(longWave,40);Throws(()=>ReminderWave.Validate(longWave));
            var original=ReminderWave.Chime(false);var duplicate=original.Concat(original.Skip(36)).ToArray();BitConverter.GetBytes(duplicate.Length-8).CopyTo(duplicate,4);Throws(()=>ReminderWave.Validate(duplicate));
        }),
        ("三档音效独立持久化与坏配置回退",()=>{
            var dir=Path.Combine(Path.GetTempPath(),"TraceTime-sounds-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{var file=Path.Combine(dir,"sounds.json");var defaults=ReminderSounds.Load(file);Check(defaults.Light.Kind=="system"&&defaults.Medium.Kind=="silent");var value=defaults.With(ReminderLevel.Severe,new("clear",30));value.Save(file);Check(ReminderSounds.Load(file).Severe==new SoundChoice("clear",30)&&ReminderSounds.Load(file).Light==defaults.Light);File.WriteAllText(file,"broken");Check(ReminderSounds.Load(file)==ReminderSounds.Default);}finally{Directory.Delete(dir,true);}
        }),
        ("导航偏好兼容缺省字段、持久化和损坏回退",()=>{
            var dir=Path.Combine(Path.GetTempPath(),"TraceTime-views-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            try{
                var file=Path.Combine(dir,"view-preferences.json");Check(TraceTime.App.ViewPreferences.Load(file)==new TraceTime.App.ViewPreferences());
                File.WriteAllText(file,"{\"SidebarCollapsed\":true}");Check(TraceTime.App.ViewPreferences.Load(file)==new TraceTime.App.ViewPreferences(true,"bar"));
                new TraceTime.App.ViewPreferences(true,"ring").Save(file);Check(TraceTime.App.ViewPreferences.Load(file)==new TraceTime.App.ViewPreferences(true,"ring"));
                new TraceTime.App.ViewPreferences(false,"invalid").Save(file);Check(TraceTime.App.ViewPreferences.Load(file).DayChart=="bar");
                File.WriteAllText(file,"broken");Check(TraceTime.App.ViewPreferences.Load(file)==new TraceTime.App.ViewPreferences());
            }finally{Directory.Delete(dir,true);}
        })
    ];
    private static void Check(bool condition){if(!condition)throw new Exception("断言失败");}
    private static void Throws(Action run){try{run();}catch(ArgumentException){return;}throw new Exception("Expected ArgumentException");}
}
