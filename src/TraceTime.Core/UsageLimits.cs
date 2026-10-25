namespace TraceTime.Core;

public enum UsageMetric { Foreground, WithoutIdle }
public enum ReminderLevel { Light, Medium, Severe }
public sealed record UsageLimit(string AppKey, int Minutes, UsageMetric Metric, ReminderLevel Level, bool Enabled = true, int Revision = 1)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AppKey) || Minutes is < 1 or > 1440 || !Enum.IsDefined(Metric) || !Enum.IsDefined(Level))
            throw new ArgumentException("请选择已记录应用、1–1440 分钟及有效的提醒设置。");
    }
}

public sealed record LimitDayState(string AppKey, DateOnly Date, int Revision, double NextSeconds, bool Pending = false, bool Dismissed = false);

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
}
