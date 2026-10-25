using TraceTime.Core;
using TraceTime.Storage;
using TraceTime.App;

var tests = new (string Name, Action Test)[]
{
    ("前台切换与多窗口归并", () => {
        var f = new Fixture(); f.Tick(0, 0); f.Through(1, 60); f.Tick(60, 0, Fixture.B); f.Through(61, 120, app: Fixture.B);
        var d = f.Report(); Near(d.Foreground.TotalSeconds, 120); Near(d.Apps.Single(x => x.App.Key == "A").Foreground.TotalSeconds, 60);
    }),
    ("达到阈值后回扣整个空闲区间", () => {
        var f = new Fixture(); f.Tick(0, 0); f.Through(1, 240); f.Through(241, 600, inputAt: 240);
        var d = f.Report(); Near(d.Foreground.TotalSeconds, 600); Near(d.WithoutIdle.TotalSeconds, 240);
    }),
    ("输入恢复后结束空闲", () => {
        var f = new Fixture(); f.Tick(0, 0); f.Through(1, 360, inputAt: 0); f.Tick(361, 0); f.Through(362, 420);
        Near(f.Report().WithoutIdle.TotalSeconds, 59);
    }),
    ("阈值前短暂无操作仍保留", () => {
        var f = new Fixture(); f.Tick(0, 0); f.Through(1, 299, inputAt: 0); Near(f.Report().WithoutIdle.TotalSeconds, 299);
    }),
    ("两次观测间刚达到阈值也能回扣", () => {
        var f = new Fixture(); f.Tick(0, 0); f.Through(1, 299, inputAt: 0); f.Tick(301, 0); Near(f.Report().WithoutIdle.TotalSeconds, 0);
    }),
    ("空闲跨应用切换只扣各自交集", () => {
        var f = new Fixture(); f.Tick(0, 0); f.Through(1, 180, inputAt: 0); f.Tick(180, 180, Fixture.B); f.Through(181, 360, inputAt: 0, app: Fixture.B);
        var d = f.Report(); Near(d.Foreground.TotalSeconds, 360); Near(d.WithoutIdle.TotalSeconds, 0); Equal(d.Apps.Count, 2);
    }),
    ("暂停或锁屏后恢复不补记空档", () => {
        var f = new Fixture(); f.Tick(0, 0); f.Through(1, 60); f.Engine.Stop(); f.Tick(660, 600); f.Through(661, 720);
        Near(f.Report().Foreground.TotalSeconds, 120); Near(f.Report().WithoutIdle.TotalSeconds, 120);
    }),
    ("漏掉电源事件时长观测空档不续记", () => {
        var f = new Fixture(); f.Tick(0, 0); f.Through(1, 60); f.Tick(660, 600); f.Through(661, 720); Near(f.Report().Foreground.TotalSeconds, 120);
    }),
    ("首次启动不借用此前空闲", () => {
        var f = new Fixture(); f.Tick(0, 1800); f.Through(1, 120, inputAt: -1800); Near(f.Report().WithoutIdle.TotalSeconds, 120);
    }),
    ("阈值变更只影响新统计段", () => {
        var f = new Fixture(); f.Tick(0, 0); f.Through(1, 240, inputAt: 0); f.Engine.ChangeThreshold(TimeSpan.FromMinutes(1));
        f.Tick(240, 240); f.Through(241, 360, inputAt: 0); Near(f.Report().WithoutIdle.TotalSeconds, 240);
    }),
    ("跨午夜按各天正确拆分", () => {
        var f = new Fixture(new DateTimeOffset(2026,10,24,23,58,0,TimeSpan.Zero)); f.Tick(0,0); f.Through(1,300);
        Near(f.Report(new(2026,10,24)).Foreground.TotalSeconds,120); Near(f.Report(new(2026,10,25)).Foreground.TotalSeconds,180);
    }),
    ("时钟前跳不凭空增加时长", () => {
        var f = new Fixture(); f.Tick(0,0); f.Through(1,60);
        f.Engine.Observe(new(f.Start.AddHours(1).AddSeconds(61),61_000,0,Fixture.A));
        f.Engine.Observe(new(f.Start.AddHours(1).AddSeconds(62),62_000,0,Fixture.A)); Near(f.Report().Foreground.TotalSeconds,61);
    }),
    ("重复空闲区间不会重复扣除", () => {
        var a = Fixture.DefaultStart; var d = ReportBuilder.Build(new(2026,10,25),TimeZoneInfo.Utc,
            [new("u","s",Fixture.A,a,a.AddMinutes(10))], [new("i","s",a,a.AddMinutes(6)),new("j","s",a.AddMinutes(4),a.AddMinutes(8))]);
        Near(d.WithoutIdle.TotalMinutes,2);
    }),
    ("存储失败可重试同一批次", () => {
        var f=new Fixture(); f.Tick(0,0); f.Through(1,10); try { f.Engine.Flush(new FailingSink()); } catch(IOException) { }
        Near(f.Report().Foreground.TotalSeconds,10);
    }),
    ("SQLite 检查点幂等与重启恢复", () => WithDatabase((db,path) => {
        var f=new Fixture(); f.Tick(0,0); f.Through(1,10); f.Engine.Flush(db); f.Through(11,20); f.Engine.Flush(db);
        Near(db.GetDay(new(2026,10,25),TimeZoneInfo.Utc).Foreground.TotalSeconds,20);
        using var reopened=new UsageDatabase(path); Near(reopened.GetDay(new(2026,10,25),TimeZoneInfo.Utc).Foreground.TotalSeconds,20);
    })),
    ("删除单日保留跨日相邻部分", () => WithDatabase((db,_) => {
        var a=new DateTimeOffset(2026,10,24,23,58,0,TimeSpan.Zero);
        db.Save(new([new("u","s",Fixture.A,a,a.AddMinutes(5))],[new("i","s",a,a.AddMinutes(5))]));
        db.DeleteRange(new DateTimeOffset(2026,10,25,0,0,0,TimeSpan.Zero),new DateTimeOffset(2026,10,26,0,0,0,TimeSpan.Zero));
        Near(db.GetDay(new(2026,10,24),TimeZoneInfo.Utc).Foreground.TotalSeconds,120); Near(db.GetDay(new(2026,10,24),TimeZoneInfo.Utc).WithoutIdle.TotalSeconds,0);
        Near(db.GetDay(new(2026,10,25),TimeZoneInfo.Utc).Foreground.TotalSeconds,0);
    })),
    ("设置持久化", () => WithDatabase((db,_)=> { db.SaveSettings(new(12)); Equal(db.LoadSettings().IdleMinutes,12); })),
    ("CSV 导出数值与公式转义", () => WithDatabase((db,path) => {
        var a=Fixture.DefaultStart; db.Save(new([new("u","s",new("A","=危险,名称"),a,a.AddSeconds(65))],[]));
        var csv=path+".csv"; db.ExportCsv(csv,new(2026,10,25),new(2026,10,25),TimeZoneInfo.Utc);
        var text=File.ReadAllText(csv); if(!text.Contains("\"'=危险,名称\",65,65")) throw new Exception(text);
    })),
    ("首次外观为跟随系统与玻璃，破损配置可恢复", () => WithDatabase((_, path) => {
        var file=path+".appearance.json";
        Equal(AppearanceSettings.Load(file),new AppearanceSettings());
        File.WriteAllText(file,"broken json"); Equal(AppearanceSettings.Load(file),new AppearanceSettings());
        File.WriteAllText(file,"{\"Mode\":\"unknown\",\"Material\":null,\"Accent\":\"red\"}");
        Equal(AppearanceSettings.Load(file),new AppearanceSettings());
    })),
    ("外观重读保留独立选择，不修改计时偏好", () => WithDatabase((db, path) => {
        db.SaveSettings(new(12)); var file=path+".appearance.json";
        foreach(var mode in AppearanceSettings.Modes)
        foreach(var material in AppearanceSettings.Materials)
        {
            new AppearanceSettings(mode,material,"#a64e6e").Save(file);
            Equal(AppearanceSettings.Load(file),new AppearanceSettings(mode,material,"#A64E6E"));
            Equal(db.LoadSettings().IdleMinutes,12);
        }
        if(File.Exists(file+".tmp")) throw new Exception("临时配置未完成替换");
    })),
    ("夏令时日期按真实日长处理", () => {
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); var (a,b)=ReportBuilder.Bounds(new(2026,3,8),zone); Near((b-a).TotalHours,23);
    })
};
var failures=0;
foreach(var (name,test) in tests) { try { test(); Console.WriteLine($"PASS {name}"); } catch(Exception e) { failures++; Console.Error.WriteLine($"FAIL {name}: {e}"); } }
Console.WriteLine($"{tests.Length-failures}/{tests.Length} passed");
return failures==0?0:1;

static void Near(double actual,double expected) { if(Math.Abs(actual-expected)>0.001) throw new Exception($"Expected {expected}, actual {actual}"); }
static void Equal<T>(T actual,T expected) { if(!EqualityComparer<T>.Default.Equals(actual,expected)) throw new Exception($"Expected {expected}, actual {actual}"); }
static void WithDatabase(Action<UsageDatabase,string> test)
{
    var dir=Path.Combine(Path.GetTempPath(),"TraceTime-tests-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
    try { var path=Path.Combine(dir,"usage.db"); using var db=new UsageDatabase(path); test(db,path); }
    finally { Directory.Delete(dir,true); }
}

sealed class Fixture
{
    public static readonly AppIdentity A=new("A","Editor"), B=new("B","Browser");
    public static readonly DateTimeOffset DefaultStart=new(2026,10,25,9,0,0,TimeSpan.Zero);
    public DateTimeOffset Start {get;}
    public TrackingEngine Engine {get;}=new(TimeSpan.FromMinutes(5));
    private readonly MemorySink _sink=new();
    public Fixture(DateTimeOffset? start=null)=>Start=start??DefaultStart;
    public void Tick(int seconds,int idle,AppIdentity? app=null)=>Engine.Observe(new(Start.AddSeconds(seconds),seconds*1000L,idle*1000L,app??A));
    public void Through(int first,int last,int? inputAt=null,AppIdentity? app=null) { for(var i=first;i<=last;i++) Tick(i,inputAt is null?0:i-inputAt.Value,app); }
    public DayReport Report(DateOnly? date=null) { Engine.Flush(_sink); return ReportBuilder.Build(date??new(2026,10,25),TimeZoneInfo.Utc,_sink.Usage.Values,_sink.Idle.Values); }
}
sealed class MemorySink:ITrackingSink
{
    public Dictionary<string,UsageSegment> Usage {get;}=new(); public Dictionary<string,IdleInterval> Idle {get;}=new();
    public void Save(ChangeBatch c) { foreach(var s in c.Usage) Usage[s.Id]=s; foreach(var s in c.Idle) Idle[s.Id]=s; }
}
sealed class FailingSink:ITrackingSink { public void Save(ChangeBatch c)=>throw new IOException("simulated disk failure"); }
