namespace TraceTime.Core;

public static class ReportBuilder
{
    public static (DateTimeOffset Start, DateTimeOffset End) Bounds(DateOnly date, TimeZoneInfo zone)
    {
        static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo tz)
        {
            while (tz.IsInvalidTime(local)) local = local.AddMinutes(1);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, tz), TimeSpan.Zero);
        }
        return (ToUtc(date.ToDateTime(TimeOnly.MinValue), zone), ToUtc(date.AddDays(1).ToDateTime(TimeOnly.MinValue), zone));
    }

    public static DayReport Build(DateOnly date, TimeZoneInfo zone, IEnumerable<UsageSegment> usage,
        IEnumerable<IdleInterval> idle, IEnumerable<GapInterval>? gaps = null)
    {
        var (start, end) = Bounds(date, zone);
        var idleByScope = idle.GroupBy(x => x.ScopeId).ToDictionary(g => g.Key, g => Merge(g.Select(x => (x.Start, x.End))));
        var totals = new Dictionary<string, AppUsage>();
        var timeline = new List<TimelineEntry>();
        foreach (var segment in usage.OrderBy(x => x.Start))
        {
            var a = Max(segment.Start, start);
            var b = Min(segment.End, end);
            if (b <= a) continue;
            var inactive = TimeSpan.Zero;
            var cursor = a;
            if (idleByScope.TryGetValue(segment.ScopeId, out var spans))
            {
                foreach (var span in spans)
                {
                    var i = Max(a, span.Start);
                    var j = Min(b, span.End);
                    if (j <= i) continue;
                    if (i > cursor) timeline.Add(new(cursor, i, segment.App.Key, segment.App.Name, "active"));
                    timeline.Add(new(i, j, segment.App.Key, segment.App.Name, "idle"));
                    inactive += j - i;
                    cursor = j;
                }
            }
            if (cursor < b) timeline.Add(new(cursor, b, segment.App.Key, segment.App.Name, "active"));
            totals.TryGetValue(segment.App.Key, out var previous);
            totals[segment.App.Key] = new(segment.App, (previous?.Foreground ?? TimeSpan.Zero) + (b - a),
                (previous?.WithoutIdle ?? TimeSpan.Zero) + (b - a - inactive));
        }
        foreach (var gap in gaps ?? [])
        {
            var a = Max(start, gap.Start); var b = Min(end, gap.End);
            if (b > a) timeline.Add(new(a, b, "", gap.Reason, "gap"));
        }
        return new(date, totals.Values.OrderByDescending(x => x.Foreground).ToArray(), timeline.OrderBy(x => x.Start).ToArray());
    }

    private static List<(DateTimeOffset Start, DateTimeOffset End)> Merge(IEnumerable<(DateTimeOffset Start, DateTimeOffset End)> spans)
    {
        var result = new List<(DateTimeOffset Start, DateTimeOffset End)>();
        foreach (var span in spans.OrderBy(x => x.Start))
        {
            if (span.End <= span.Start) continue;
            if (result.Count == 0 || result[^1].End < span.Start) result.Add(span);
            else result[^1] = (result[^1].Start, Max(result[^1].End, span.End));
        }
        return result;
    }

    public static DateTimeOffset Max(DateTimeOffset x, DateTimeOffset y) => x > y ? x : y;
    public static DateTimeOffset Min(DateTimeOffset x, DateTimeOffset y) => x < y ? x : y;
}
