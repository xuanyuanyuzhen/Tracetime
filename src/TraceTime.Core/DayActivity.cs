namespace TraceTime.Core;

public sealed record ActivitySlice(DateTimeOffset Start, DateTimeOffset End, IReadOnlyList<TimelineEntry> Entries, IReadOnlyList<AppUsage> Apps)
{
    public TimeSpan Active => Sum("active");
    public TimeSpan Idle => Sum("idle");
    public TimeSpan Interrupted => Sum("gap");
    public TimeSpan Unrecorded => Sum("unrecorded");
    public TimeSpan Future => Sum("future");
    public TimeSpan Foreground => Active + Idle;
    private TimeSpan Sum(string kind) => TimeSpan.FromTicks(Entries.Where(x => x.Kind == kind).Sum(x => (x.End - x.Start).Ticks));
}

public sealed record ActivityHour(int Index, DateTimeOffset Start, DateTimeOffset End, string Label, ActivitySlice Slice);

public sealed class DayActivity
{
    private readonly Dictionary<string, AppIdentity> _apps;
    public DateTimeOffset Start { get; }
    public DateTimeOffset End { get; }
    public IReadOnlyList<TimelineEntry> Entries { get; }
    public IReadOnlyList<ActivityHour> Hours { get; }
    public ActivitySlice Whole { get; }

    public DayActivity(DayReport report, TimeZoneInfo zone, DateTimeOffset observedUntil)
    {
        (Start, End) = ReportBuilder.Bounds(report.Date, zone);
        _apps = report.Apps.ToDictionary(x => x.App.Key, x => x.App);
        var cutoff = ReportBuilder.Min(End, ReportBuilder.Max(Start, observedUntil));
        var source = report.Timeline.Where(x => x.Kind is "active" or "idle" or "gap")
            .Select(x => x with { Start = ReportBuilder.Max(Start, x.Start), End = ReportBuilder.Min(cutoff, x.End) }).Where(x => x.End > x.Start).ToArray();
        // Foreground evidence wins over overlapping interruption records. Never double-count a moment.
        var events = source.SelectMany((x, i) => new[] { (At: x.Start, Id: i, Add: true), (At: x.End, Id: i, Add: false) }).OrderBy(x => x.At).GroupBy(x => x.At);
        var active = new SortedSet<(int Priority, int Id)>(); var cursor = Start; var entries = new List<TimelineEntry>();
        var previousSource = -2;
        void AddUntil(DateTimeOffset end)
        {
            if (end <= cursor) return;
            var sourceId = active.Count == 0 ? -1 : active.Min.Id;
            var entry = sourceId < 0 ? new TimelineEntry(cursor, end, "", "未运行 / 未记录", "unrecorded") : source[sourceId] with { Start = cursor, End = end };
            // Preserve distinct recorded slices, even when consecutive slices have the same app/state.
            // Only rejoin one source split by a lower-priority overlapping event.
            if (entries.Count > 0 && previousSource == sourceId && entries[^1].End == entry.Start) entries[^1] = entries[^1] with { End = end };
            else entries.Add(entry);
            cursor = end; previousSource = sourceId;
        }
        foreach (var group in events)
        {
            AddUntil(group.Key);
            foreach (var point in group) { var item = (source[point.Id].Kind == "gap" ? 1 : 0, point.Id); if (point.Add) active.Add(item); else active.Remove(item); }
        }
        AddUntil(cutoff);
        if (cutoff < End) entries.Add(new(cutoff, End, "", "尚未到来", "future"));
        Entries = entries;
        Whole = Slice(Start, End);
        var hours = new List<ActivityHour>();
        for (var a = Start; a < End; a = a.AddHours(1))
        {
            var b = ReportBuilder.Min(a.AddHours(1), End); var local = TimeZoneInfo.ConvertTime(a, zone);
            var endLabel = b == End ? "24:00" : TimeZoneInfo.ConvertTime(b, zone).ToString("HH:mm");
            var zoneLabel = (End - Start).TotalHours == 24 ? "" : $" ({local:zzz})";
            hours.Add(new(hours.Count, a, b, $"{local:HH:mm}–{endLabel}{zoneLabel}", Slice(a, b)));
        }
        Hours = hours;
    }

    public ActivitySlice Slice(DateTimeOffset start, DateTimeOffset end)
    {
        start = ReportBuilder.Max(Start, start); end = ReportBuilder.Min(End, end);
        if (end < start) throw new ArgumentException("时段终点不能早于起点。");
        var entries = Entries.Where(x => x.Start < end && x.End > start)
            .Select(x => x.Start >= start && x.End <= end ? x : x with { Start = ReportBuilder.Max(start, x.Start), End = ReportBuilder.Min(end, x.End) }).ToArray();
        var apps = entries.Where(x => x.Kind is "active" or "idle").GroupBy(x => x.AppKey).Select(g => new AppUsage(
            _apps.GetValueOrDefault(g.Key) ?? new(g.Key, g.First().Label), TimeSpan.FromTicks(g.Sum(x => (x.End - x.Start).Ticks)),
            TimeSpan.FromTicks(g.Where(x => x.Kind == "active").Sum(x => (x.End - x.Start).Ticks)))).OrderByDescending(x => x.Foreground).ToArray();
        return new(start, end, entries, apps);
    }
}
