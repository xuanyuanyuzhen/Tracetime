namespace TraceTime.Core;

public sealed record DueLimit(UsageLimit Rule, LimitDayState State, AppUsage Usage, double Seconds);

public static class LimitDispatch
{
    public static DueLimit? NextForVisit(IReadOnlyList<UsageLimit> rules, DayReport report, Func<UsageLimit, LimitDayState> loadState, LimitVisits visits, bool canPresent)
    {
        if (!canPresent) return null;
        foreach (var rule in rules.Where(x => x.Enabled).OrderByDescending(x => x.Level))
        {
            var usage = report.Apps.FirstOrDefault(x => x.App.Key == rule.AppKey);
            if (usage is null) continue;
            var state = loadState(rule);
            if (!state.Pending && rule.AppKey != visits.CurrentAppKey) continue;
            var seconds = LimitPolicy.Seconds(usage, rule.Metric);
            if (LimitPolicy.ShouldPresent(rule, state, seconds, usage.Foreground.TotalSeconds, visits.IsNewVisit(rule, report.Date)))
                return new(rule, state, usage, seconds);
        }
        return null;
    }
    public static DueLimit? Next(IReadOnlyList<UsageLimit> rules, DayReport report, Func<UsageLimit, LimitDayState> loadState, bool canPresent)
    {
        if (!canPresent) return null;
        foreach (var rule in rules.Where(x => x.Enabled).OrderByDescending(x => x.Level))
        {
            var usage = report.Apps.FirstOrDefault(x => x.App.Key == rule.AppKey);
            if (usage is null) continue;
            var state = loadState(rule); var seconds = LimitPolicy.Seconds(usage,rule.Metric);
            if (LimitPolicy.IsDue(rule,state,seconds)) return new(rule,state,usage,seconds);
        }
        return null;
    }
}
