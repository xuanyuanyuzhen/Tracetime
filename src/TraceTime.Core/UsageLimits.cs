namespace TraceTime.Core;

public enum UsageMetric { Foreground, WithoutIdle }
public enum ReminderLevel { Light, Medium, Severe }
public sealed record UsageLimit(string AppKey, int Minutes, UsageMetric Metric, ReminderLevel Level, bool Enabled = true, int Revision = 1, bool RepeatOnEntry = true, bool EmailEnabled = false)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AppKey) || Minutes is < 1 or > 1440 || !Enum.IsDefined(Metric) || !Enum.IsDefined(Level))
            throw new ArgumentException("请选择已记录应用、1–1440 分钟及有效的提醒设置。");
    }
}

public sealed record LimitDayState(string AppKey, DateOnly Date, int Revision, double NextSeconds, bool Pending = false, bool Dismissed = false,
    bool Acknowledged = false, double? TemporaryUntilForeground = null, string? TemporaryToken = null, bool TemporaryNotified = false);

// All decisions are deterministic and independent of Windows, wall-clock timers and UI.
public static class LimitPolicy
{
    public static LimitDayState ForDay(UsageLimit rule, DateOnly date, LimitDayState? saved) =>
        saved is not null && saved.AppKey == rule.AppKey && saved.Date == date && saved.Revision == rule.Revision
            ? saved : new(rule.AppKey, date, rule.Revision, rule.Minutes * 60d);
    public static double Seconds(AppUsage? usage, UsageMetric metric) => Math.Max(0, usage is null ? 0 : (metric == UsageMetric.Foreground ? usage.Foreground : usage.WithoutIdle).TotalSeconds);
    public static bool IsDue(UsageLimit rule, LimitDayState state, double seconds) => rule.Enabled && !state.Dismissed && (state.Pending || seconds >= state.NextSeconds);
    public static LimitDayState Begin(LimitDayState state) => state with { Pending = true };
    public static LimitDayState Dismiss(LimitDayState state) => state with { Pending = false, Dismissed = true };
    public static LimitDayState Snooze(LimitDayState state, double currentSeconds) => state with { Pending = false, Dismissed = false, NextSeconds = Math.Max(state.NextSeconds, currentSeconds) + 600 };

    public static bool TemporaryActive(LimitDayState state, double foreground) => state.TemporaryUntilForeground is { } until && foreground < until;
    public static bool TemporaryExpired(LimitDayState state, double foreground) => state.TemporaryUntilForeground is { } until && foreground >= until;
    public static bool ShouldPresent(UsageLimit rule, LimitDayState state, double seconds, double foreground, bool newVisit)
    {
        if (!rule.Enabled || state.Dismissed || TemporaryActive(state, foreground)) return false;
        return state.Pending || (TemporaryExpired(state, foreground) && !state.TemporaryNotified)
            || (seconds >= state.NextSeconds && (!state.Acknowledged || (rule.RepeatOnEntry && newVisit)));
    }
    public static LimitDayState StartTemporary(LimitDayState state, double foreground, int minutes, string token)
    {
        if (minutes is < 1 or > 120 || !double.IsFinite(foreground) || foreground < 0 || string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("临时使用须为 1–120 分钟。");
        return state with { Pending = false, Dismissed = false, Acknowledged = false,
            TemporaryUntilForeground = foreground + minutes * 60d, TemporaryToken = token, TemporaryNotified = false };
    }
    public static LimitDayState CancelTemporary(LimitDayState state) => state with {
        TemporaryUntilForeground = null, TemporaryToken = null, TemporaryNotified = false, Acknowledged = false, Pending = false };
    public static LimitDayState BeginPresentation(LimitDayState state, double foreground, double? seconds = null) => state with {
        Pending = true, Acknowledged = state.Acknowledged || seconds >= state.NextSeconds,
        TemporaryNotified = state.TemporaryNotified || TemporaryExpired(state, foreground) };
    public static LimitDayState Acknowledge(LimitDayState state, double seconds) => state with { Pending = false, Acknowledged = state.Acknowledged || seconds >= state.NextSeconds };
}

// Ignore our own windows: a severe reminder requesting focus must not create another visit.
public sealed class LimitVisits
{
    private long _visit;
    private readonly Dictionary<string, (DateOnly Day, int Revision, long Visit)> _presented = new();
    public string? CurrentAppKey { get; private set; }
    public bool Observe(string? key, bool available, bool ownWindow = false)
    {
        if (available && ownWindow) return false;
        var next = available ? key : null;
        if (next == CurrentAppKey) return false;
        CurrentAppKey = next; _visit++; return true;
    }
    public bool IsNewVisit(UsageLimit rule, DateOnly day) => !_presented.TryGetValue(rule.AppKey, out var seen)
        || seen != (day, rule.Revision, _visit);
    public void Presented(UsageLimit rule, DateOnly day) => _presented[rule.AppKey] = (day, rule.Revision, _visit);
}
