namespace TraceTime.Core;

public sealed record UsageShare(string Key, string Name, TimeSpan Foreground, TimeSpan WithoutIdle, int AppCount = 1, bool IsOther = false)
{
    public TimeSpan Duration(bool withoutIdle) => withoutIdle ? WithoutIdle : Foreground;
}

public static class UsageDistribution
{
    public static IReadOnlyList<UsageShare> Build(IReadOnlyList<AppUsage> apps, bool withoutIdle)
    {
        var sorted = apps.Where(x => (withoutIdle ? x.WithoutIdle : x.Foreground) > TimeSpan.Zero)
            .OrderByDescending(x => withoutIdle ? x.WithoutIdle : x.Foreground).ThenBy(x => x.App.Key, StringComparer.Ordinal).ToArray();
        var leaders = sorted.Length <= 6 ? sorted.Length : 5;
        var shares = sorted.Take(leaders).Select(x => new UsageShare(x.App.Key, x.App.Name, x.Foreground, x.WithoutIdle)).ToList();
        if (sorted.Length > leaders)
        {
            var rest = sorted.Skip(leaders).ToArray();
            shares.Add(new("", $"其余 {rest.Length} 个应用", TimeSpan.FromTicks(rest.Sum(x => x.Foreground.Ticks)),
                TimeSpan.FromTicks(rest.Sum(x => x.WithoutIdle.Ticks)), rest.Length, true));
        }
        return shares;
    }
}
