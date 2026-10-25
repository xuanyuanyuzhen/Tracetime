namespace TraceTime.Core;

public sealed record AppIdentity(string Key, string Name, string? ExecutablePath = null)
{
    public static readonly AppIdentity Unknown = new("unknown", "未识别应用");
    public static readonly AppIdentity System = new("windows-system", "Windows 系统界面");
}

public sealed record UsageSegment(string Id, string ScopeId, AppIdentity App, DateTimeOffset Start, DateTimeOffset End, long ThresholdMs = 300000);
public sealed record IdleInterval(string Id, string ScopeId, DateTimeOffset Start, DateTimeOffset End);
public sealed record GapInterval(string Id, DateTimeOffset Start, DateTimeOffset End, string Reason);
public sealed record TrackingSample(DateTimeOffset Utc, long MonotonicMs, long IdleMs, AppIdentity App);
public sealed record ChangeBatch(IReadOnlyList<UsageSegment> Usage, IReadOnlyList<IdleInterval> Idle);

public interface ITrackingSink { void Save(ChangeBatch changes); }

public sealed record AppUsage(AppIdentity App, TimeSpan Foreground, TimeSpan WithoutIdle);
public sealed record TimelineEntry(DateTimeOffset Start, DateTimeOffset End, string AppKey, string Label, string Kind);
public sealed record DayReport(DateOnly Date, IReadOnlyList<AppUsage> Apps, IReadOnlyList<TimelineEntry> Timeline)
{
    public TimeSpan Foreground => TimeSpan.FromTicks(Apps.Sum(x => x.Foreground.Ticks));
    public TimeSpan WithoutIdle => TimeSpan.FromTicks(Apps.Sum(x => x.WithoutIdle.Ticks));
}

public sealed record AppSettings(int IdleMinutes = 5);
