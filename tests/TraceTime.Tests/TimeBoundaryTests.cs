using TraceTime.Core;

internal static class TimeBoundaryTests
{
    // Fixed rules isolate these scenarios from OS time-zone updates and system settings.
    internal static readonly TimeZoneInfo Eastern = TimeZoneInfo.CreateCustomTimeZone(
        "TraceTime-test-Eastern", TimeSpan.FromHours(-5), "Test Eastern", "Standard", "Daylight",
        [TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new(2026,1,1), new(2026,12,31), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new(1,1,1,2,0,0),3,8),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new(1,1,1,2,0,0),11,1))]);

    public static IEnumerable<(string Name, Action Test)> All => new (string, Action)[]
    {
        ("秋季DST重复小时保留25小时和双口径守恒", () =>
        {
            var date = new DateOnly(2026,11,1); var (a,b) = ReportBuilder.Bounds(date,Eastern);
            Equal(b-a,TimeSpan.FromHours(25));
            var report = ReportBuilder.Build(date,Eastern,[new("u","s",Fixture.A,a,b)],
                [new("i","s",a.AddHours(1),a.AddHours(3))]);
            var activity = new DayActivity(report,Eastern,b);
            Equal(activity.Hours.Count,25);
            Equal(report.Foreground,TimeSpan.FromHours(25)); Equal(report.WithoutIdle,TimeSpan.FromHours(23));
            Equal(activity.Hours.Sum(x=>x.Slice.Foreground.Ticks),report.Foreground.Ticks);
            Equal(activity.Hours.Sum(x=>x.Slice.Active.Ticks),report.WithoutIdle.Ticks);
        }),
        ("墙钟回拨一小时切Scope且不制造负时长", () =>
        {
            var engine = new TrackingEngine(TimeSpan.FromMinutes(5)); var sink = new MemorySink();
            var start = Fixture.DefaultStart;
            for(var i=0;i<=3600;i++) engine.Observe(new(start.AddSeconds(i),i*1000L,0,Fixture.A));
            for(var i=3601;i<=3661;i++) engine.Observe(new(start.AddSeconds(i-3600),i*1000L,0,Fixture.A));
            engine.Flush(sink);
            Equal(sink.Usage.Values.Select(x=>x.ScopeId).Distinct().Count(),2);
            Equal(sink.Usage.Values.Sum(x=>(x.End-x.Start).Ticks),TimeSpan.FromSeconds(3660).Ticks);
            if(sink.Usage.Values.Any(x=>x.End<=x.Start)) throw new Exception("出现非正区间");
            // Re-anchoring can overlap prior wall-clock evidence; daily totals retain both scopes.
            var ranges = sink.Usage.Values.OrderBy(x=>x.Start).ToArray();
            if(ranges[1].Start>=ranges[0].End) throw new Exception("回拨现状假设改变，请复核时间ADR");
        }),
        ("小幅NTP回拨保留单调计时与两秒阈值", () =>
        {
            foreach(var drift in new[]{1500,2000,2001})
            {
                var engine = new TrackingEngine(TimeSpan.FromMinutes(5)); var sink = new MemorySink();
                engine.Observe(new(Fixture.DefaultStart,0,0,Fixture.A));
                engine.Observe(new(Fixture.DefaultStart.AddSeconds(1),1000,0,Fixture.A));
                engine.Observe(new(Fixture.DefaultStart.AddMilliseconds(2000-drift),2000,0,Fixture.A));
                engine.Observe(new(Fixture.DefaultStart.AddMilliseconds(3000-drift),3000,0,Fixture.A));
                engine.Flush(sink);
                Equal(sink.Usage.Values.Select(x=>x.ScopeId).Distinct().Count(),drift<=2000?1:2);
                Equal(sink.Usage.Values.Sum(x=>(x.End-x.Start).Ticks),TimeSpan.FromSeconds(drift<=2000?3:2).Ticks);
            }
        }),
        ("模拟休眠叠加调时与单调时钟回退均切断", () =>
        {
            foreach(var stop in new[]{false,true})
            foreach(var mono in new[]{-1000L,660000L})
            {
                var engine = new TrackingEngine(TimeSpan.FromMinutes(5)); var sink = new MemorySink();
                engine.Observe(new(Fixture.DefaultStart,0,0,Fixture.A));
                engine.Observe(new(Fixture.DefaultStart.AddSeconds(10),10000,0,Fixture.A));
                if(stop)engine.Stop();
                var resume = mono<0 ? Fixture.DefaultStart.AddMilliseconds(mono) : Fixture.DefaultStart.AddHours(-1);
                engine.Observe(new(resume,mono,600000,Fixture.A));
                engine.Observe(new(resume.AddSeconds(10),mono+10000,610000,Fixture.A));
                engine.Flush(sink);
                Equal(sink.Usage.Values.Sum(x=>(x.End-x.Start).Ticks),TimeSpan.FromSeconds(20).Ticks);
                Equal(sink.Idle.Count,0);
            }
        }),
        ("查询时区改变日期归属而不改变原始UTC证据", () =>
        {
            var date = new DateOnly(2026,10,25); var a = new DateTimeOffset(2026,10,25,23,58,0,TimeSpan.Zero);
            UsageSegment[] usage = [new("u","s",Fixture.A,a,a.AddMinutes(5))];
            var east = TimeZoneInfo.CreateCustomTimeZone("TraceTime-test-UTC8",TimeSpan.FromHours(8),"UTC8","UTC8");
            Equal(ReportBuilder.Build(date,TimeZoneInfo.Utc,usage,[]).Foreground,TimeSpan.FromMinutes(2));
            Equal(ReportBuilder.Build(date.AddDays(1),TimeZoneInfo.Utc,usage,[]).Foreground,TimeSpan.FromMinutes(3));
            Equal(ReportBuilder.Build(date,east,usage,[]).Foreground,TimeSpan.Zero);
            Equal(ReportBuilder.Build(date.AddDays(1),east,usage,[]).Foreground,TimeSpan.FromMinutes(5));
            Equal(usage[0].Start,a);
        }),
        ("确定性随机区间跨DST分日与空闲合并逐tick守恒", () =>
        {
            var random = new Random(41621); var first = new DateOnly(2026,10,31);
            var (start,_) = ReportBuilder.Bounds(first,Eastern);
            for(var trial=0;trial<200;trial++)
            {
                var a = start.AddTicks(random.NextInt64(TimeSpan.FromHours(12).Ticks));
                var b = a.AddTicks(random.NextInt64(1,TimeSpan.FromHours(48).Ticks));
                var i = a.AddTicks((b-a).Ticks/4); var j = a.AddTicks((b-a).Ticks*3/4);
                UsageSegment[] usage = [new("u","s",Fixture.A,a,b)];
                IdleInterval[] idle = [new("i","s",i,j),new("duplicate","s",i,j),new("other","other-scope",a,b)];
                var days = Enumerable.Range(0,4).Select(n=>ReportBuilder.Build(first.AddDays(n),Eastern,usage,idle)).ToArray();
                Equal(days.Sum(x=>x.Foreground.Ticks),(b-a).Ticks);
                Equal(days.Sum(x=>x.WithoutIdle.Ticks),(b-a-(j-i)).Ticks);
                foreach(var day in days)
                    if(day.WithoutIdle<TimeSpan.Zero || day.WithoutIdle>day.Foreground)throw new Exception("双口径越界");
            }
        })
    };

    private static void Equal<T>(T actual,T expected)
    {
        if(!EqualityComparer<T>.Default.Equals(actual,expected))throw new Exception($"Expected {expected}, actual {actual}");
    }
}
