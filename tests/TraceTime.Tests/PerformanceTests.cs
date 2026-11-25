using TraceTime.Core;
using TraceTime.Storage;

internal static class PerformanceTests
{
    public static IEnumerable<(string Name, Action Test)> All => new (string, Action)[]
    {
        ("全天汇总反复读取不重复分配且小时合计一致", () =>
        {
            var date = new DateOnly(2026,11,10); var (start,end) = ReportBuilder.Bounds(date,TimeZoneInfo.Utc);
            var usage = Enumerable.Range(0,3600).Select(i => new UsageSegment(i.ToString(),i.ToString(),new("app"+i%40,"应用"+i%40),start.AddSeconds(i*10),start.AddSeconds(i*10+10))).ToArray();
            var activity = new DayActivity(ReportBuilder.Build(date,TimeZoneInfo.Utc,usage,[]),TimeZoneInfo.Utc,end);
            var whole = activity.Whole;
            var before = GC.GetAllocatedBytesForCurrentThread(); long ticks=0;
            for(var i=0;i<1000;i++) ticks += activity.Whole.End.Ticks;
            var allocated = GC.GetAllocatedBytesForCurrentThread()-before;
            if(allocated>8192 || ticks==0) throw new Exception($"重复汇总产生过多分配：{allocated}");
            if(whole.Foreground.Ticks!=activity.Hours.Sum(x=>x.Slice.Foreground.Ticks) || whole.Entries.Count(x=>x.Kind=="active")!=3600) throw new Exception("全天/小时合计或原始时间片改变");
            var clipped=activity.Slice(start.AddSeconds(5),start.AddSeconds(15));
            if(clipped.Foreground.TotalSeconds!=10 || clipped.Entries.Count!=2 || whole.Foreground.TotalHours!=10) throw new Exception("裁剪改变完整汇总或边界");
        })
    };

    internal static void Seed(string directory, int days = 1, bool withRules = false)
    {
        if(days is < 1 or > 90)throw new ArgumentOutOfRangeException(nameof(days));
        var path=Path.GetFullPath(directory);
        if(Directory.Exists(path)) throw new ArgumentException("性能夹具只允许创建不存在的新目录。");
        using var db=new UsageDatabase(Path.Combine(path,"usage.db"));
        var (start,_)=ReportBuilder.Bounds(DateOnly.FromDateTime(DateTime.Today),TimeZoneInfo.Local);
        for(var day=0;day<days;day++)
        {
        var usage=new List<UsageSegment>();var idle=new List<IdleInterval>();
        for(var i=0;i<3600;i++)
        {
            var a=start.AddDays(-day).AddHours(2).AddSeconds(i*10);var id=$"{day}-{i}";
            usage.Add(new(id,id,new($"synthetic-{i%40:00}",$"Synthetic app {i%40:00}"),a,a.AddSeconds(10)));
            if(i%5==0)idle.Add(new(id,id,a.AddSeconds(5),a.AddSeconds(10)));
        }
        db.Save(new(usage,idle));
        }
        if(withRules)
            for(var i=0;i<40;i++) db.SaveLimit(new($"synthetic-{i:00}",1440,UsageMetric.Foreground,ReminderLevel.Light));
        Console.WriteLine($"Seeded {days} days / {days*3600} synthetic usage segments / {days*720} idle intervals / 40 apps / {(withRules?40:0)} rules.");
    }

    internal static void MeasureSummary(string directory)
    {
        Seed(directory,90,true);
        using var db = new UsageDatabase(Path.Combine(directory,"usage.db"));
        var date = DateOnly.FromDateTime(DateTime.Today); var zone = TimeZoneInfo.Local;
        var rules = db.LoadLimits(); var visits = new LimitVisits(); visits.Observe("synthetic-00",true);
        var measurements = new List<object>();
        // Alternate the reference and summary path to reduce order/JIT bias. Reads use the same DB.
        var samples = new Dictionary<string,List<(double Ms,long Bytes)>> { ["full"]=[], ["summary"]=[] };
        for(var i=0;i<36;i++)
        foreach(var mode in i%2==0?new[]{"full","summary"}:new[]{"summary","full"})
        {
            var allocated = GC.GetAllocatedBytesForCurrentThread(); var watch = System.Diagnostics.Stopwatch.StartNew();
            var report = mode=="full"?db.GetDay(date,zone):db.GetDaySummary(date,zone);
            var due = LimitDispatch.NextForVisit(rules,report,r=>db.LoadLimitDay(r,date),visits,true);
            watch.Stop(); var bytes = GC.GetAllocatedBytesForCurrentThread()-allocated;
            if(due is not null)throw new Exception("合成规则不应触发提醒");
            if(i>=6)samples[mode].Add((watch.Elapsed.TotalMilliseconds,bytes));
        }
        SummaryTests.Same(db.GetDay(date,zone),db.GetDaySummary(date,zone));
        foreach(var (mode,values) in samples)
        {
            var sorted=values.Select(x=>x.Ms).Order().ToArray();
            measurements.Add(new { mode, calls=values.Count, medianMs=(sorted[14]+sorted[15])/2, p95Ms=sorted[28],
                meanAllocatedBytes=values.Average(x=>x.Bytes), usageReadRows=3600, idleReadRows=720 });
        }
        var result = new { days=90,segments=324000,idleIntervals=64800,apps=40,rules=40,
            runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            note="Warm in-process reads plus reminder selection; per-thread managed allocation, not resident memory or physical I/O.",measurements };
        var json=System.Text.Json.JsonSerializer.Serialize(result,new System.Text.Json.JsonSerializerOptions { WriteIndented=true });
        File.WriteAllText(Path.Combine(directory,"summary-benchmark.json"),json); Console.WriteLine(json);
    }
}
