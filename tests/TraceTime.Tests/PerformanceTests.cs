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

    internal static void Seed(string directory)
    {
        var path=Path.GetFullPath(directory);
        if(Directory.Exists(path)) throw new ArgumentException("性能夹具只允许创建不存在的新目录。");
        using var db=new UsageDatabase(Path.Combine(path,"usage.db"));
        var (start,_)=ReportBuilder.Bounds(DateOnly.FromDateTime(DateTime.Today),TimeZoneInfo.Local);
        var usage=new List<UsageSegment>();var idle=new List<IdleInterval>();
        for(var i=0;i<3600;i++)
        {
            var a=start.AddHours(2).AddSeconds(i*10);var id=i.ToString();
            usage.Add(new(id,id,new($"synthetic-{i%40:00}",$"Synthetic app {i%40:00}"),a,a.AddSeconds(10)));
            if(i%5==0)idle.Add(new(id,id,a.AddSeconds(5),a.AddSeconds(10)));
        }
        db.Save(new(usage,idle));
        Console.WriteLine("Seeded 3600 synthetic usage segments / 720 idle intervals / 40 apps.");
    }
}
