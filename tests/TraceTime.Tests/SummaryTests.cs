using TraceTime.Core;
using TraceTime.Storage;

internal static class SummaryTests
{
    public static IEnumerable<(string Name, Action Test)> All => new (string, Action)[]
    {
        ("纯汇总与完整报告在乱序跨日空闲和Scope重叠时一致", () =>
        {
            var date = new DateOnly(2026,11,1); var (a,b) = ReportBuilder.Bounds(date,TimeBoundaryTests.Eastern);
            var random = new Random(1837);
            var usage = Enumerable.Range(0,180).Select(i=>new UsageSegment($"u{i}",$"s{i%7}",new($"app{i%9}",$"名称{i}"),
                a.AddMinutes(random.Next(-60,1500)),a.AddMinutes(random.Next(1501,1560)))).Reverse().ToArray();
            var idle = Enumerable.Range(0,100).Select(i=>new IdleInterval($"i{i}",$"s{i%8}",a.AddMinutes(i*7),a.AddMinutes(i*7+70))).ToArray();
            for(var day=-1;day<=2;day++)
            {
                var d = date.AddDays(day);
                Same(ReportBuilder.Build(d,TimeBoundaryTests.Eastern,usage,idle,[new("gap",a,b,"sleep")]),
                    ReportBuilder.BuildSummary(d,TimeBoundaryTests.Eastern,usage,idle));
            }
        }),
        ("纯汇总立即反映检查点空闲回扣删除恢复与重启", () => WithDatabase((db,path)=>
        {
            var date = new DateOnly(2026,10,25); var a = Fixture.DefaultStart;
            void Check(double foreground,double active)
            {
                var summary = db.GetDaySummary(date,TimeZoneInfo.Utc); Same(db.GetDay(date,TimeZoneInfo.Utc),summary);
                if(summary.Foreground.TotalSeconds!=foreground || summary.WithoutIdle.TotalSeconds!=active)throw new Exception("汇总未反映最新原始数据");
            }
            Check(0,0);
            var segment = new UsageSegment("u","s",Fixture.A,a,a.AddSeconds(10));
            db.Save(new([segment],[])); Check(10,10);
            db.Save(new([segment with { End=a.AddSeconds(20) }],[])); Check(20,20);
            db.Save(new([],[new("i","s",a,a.AddSeconds(15))])); Check(20,5);
            var archivePath = path+".ttarchive";
            var archive = UsageDatabase.CreateHistoryArchive(path,archivePath,TimeZoneInfo.Utc);
            db.DeleteRange(a.AddSeconds(5),a.AddSeconds(10),TimeZoneInfo.Utc); Check(15,5);
            db.RestoreHistoryArchive(archivePath,archive.Sha256,path+".safety.ttarchive",date,TimeZoneInfo.Utc); Check(20,5);
            using var reopened = new UsageDatabase(path); Same(db.GetDay(date,TimeZoneInfo.Utc),reopened.GetDaySummary(date,TimeZoneInfo.Utc));
            if(reopened.GetDaySummary(date.AddDays(1),TimeZoneInfo.Utc).Apps.Count!=0)throw new Exception("跨日复用旧汇总");
        })),
        ("提醒纯汇总保持再进入临时额度与邮件选择结果", () =>
        {
            var date = new DateOnly(2026,10,25); var a = Fixture.DefaultStart;
            UsageSegment[] usage = [new("u","s",Fixture.A,a,a.AddMinutes(10))];
            IdleInterval[] idle = [new("i","s",a,a.AddMinutes(4))];
            var full = ReportBuilder.Build(date,TimeZoneInfo.Utc,usage,idle);
            var summary = ReportBuilder.BuildSummary(date,TimeZoneInfo.Utc,usage,idle);
            var visits = new LimitVisits(); visits.Observe("A",true);
            foreach(var metric in Enum.GetValues<UsageMetric>())
            {
                var rule = new UsageLimit("A",5,metric,ReminderLevel.Medium,EmailEnabled:true);
                var initial = LimitPolicy.ForDay(rule,date,null);
                foreach(var state in new[]{initial,LimitPolicy.Acknowledge(initial,600),LimitPolicy.StartTemporary(initial,600,1,"temp"),
                    LimitPolicy.StartTemporary(initial,0,1,"expired"),LimitPolicy.Dismiss(initial),initial with { Pending=true }})
                {
                    var expected = LimitDispatch.NextForVisit([rule],full,_=>state,visits,true);
                    var actual = LimitDispatch.NextForVisit([rule],summary,_=>state,visits,true);
                    if(expected!=actual)throw new Exception("提醒选择改变");
                    if(!EmailNoticePolicy.Due(rule,state,full.Apps[0]).SequenceEqual(EmailNoticePolicy.Due(rule,state,summary.Apps[0])))throw new Exception("邮件选择改变");
                }
            }
        }),
        ("纯汇总避免时间线分配且保留全部应用总量", () =>
        {
            var date = new DateOnly(2026,10,25); var a = Fixture.DefaultStart;
            var usage = Enumerable.Range(0,3600).Select(i=>new UsageSegment($"u{i}","s",new($"app{i%40}",$"App {i%40}"),a.AddSeconds(i),a.AddSeconds(i+1))).ToArray();
            for(var i=0;i<3;i++) { ReportBuilder.Build(date,TimeZoneInfo.Utc,usage,[]); ReportBuilder.BuildSummary(date,TimeZoneInfo.Utc,usage,[]); }
            var before = GC.GetAllocatedBytesForCurrentThread(); var full = ReportBuilder.Build(date,TimeZoneInfo.Utc,usage,[]);
            var fullBytes = GC.GetAllocatedBytesForCurrentThread()-before;
            before = GC.GetAllocatedBytesForCurrentThread(); var summary = ReportBuilder.BuildSummary(date,TimeZoneInfo.Utc,usage,[]);
            var summaryBytes = GC.GetAllocatedBytesForCurrentThread()-before;
            Same(full,summary);
            if(summaryBytes>=fullBytes*0.75)throw new Exception($"纯汇总分配未下降至少25%: {fullBytes} -> {summaryBytes}");
        })
    };

    internal static void Same(DayReport full,DayReport summary)
    {
        if(full.Date!=summary.Date || !full.Apps.SequenceEqual(summary.Apps) || summary.Timeline.Count!=0 ||
            full.Foreground!=summary.Foreground || full.WithoutIdle!=summary.WithoutIdle)throw new Exception("双口径汇总或应用身份/顺序不一致");
    }

    private static void WithDatabase(Action<UsageDatabase,string> test)
    {
        var dir = Path.Combine(Path.GetTempPath(),"TraceTime-summary-"+Guid.NewGuid().ToString("N"));
        try { var path=Path.Combine(dir,"usage.db"); using var db = new UsageDatabase(path); test(db,path); }
        finally { Directory.Delete(dir,true); }
    }
}
