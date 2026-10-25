using TraceTime.Core;

namespace TraceTime.Storage;

public sealed partial class UsageDatabase
{
    private void InitializeLimits() => Execute("""
        CREATE TABLE IF NOT EXISTS usage_limits (app_key TEXT PRIMARY KEY, minutes INTEGER NOT NULL, metric INTEGER NOT NULL, level INTEGER NOT NULL, enabled INTEGER NOT NULL, revision INTEGER NOT NULL);
        CREATE TABLE IF NOT EXISTS limit_days (app_key TEXT NOT NULL, day TEXT NOT NULL, revision INTEGER NOT NULL, next_seconds REAL NOT NULL, pending INTEGER NOT NULL, dismissed INTEGER NOT NULL, PRIMARY KEY(app_key,day));
        CREATE INDEX IF NOT EXISTS usage_app_time ON usage(app_key,end_ticks DESC);
        PRAGMA user_version=2;
        """);

    public IReadOnlyList<AppIdentity> RecordedApps()
    {
        using var cmd = Command("SELECT app_key,name,path FROM (SELECT app_key,name,path,ROW_NUMBER() OVER(PARTITION BY app_key ORDER BY end_ticks DESC) AS rn FROM usage WHERE end_ticks>start_ticks) WHERE rn=1 ORDER BY name COLLATE NOCASE");
        using var r = cmd.ExecuteReader(); var apps = new List<AppIdentity>();
        while (r.Read()) apps.Add(new(r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2)));
        return apps;
    }
    public IReadOnlyList<UsageLimit> LoadLimits()
    {
        using var cmd = Command("SELECT app_key,minutes,metric,level,enabled,revision FROM usage_limits ORDER BY app_key");
        using var r = cmd.ExecuteReader(); var result = new List<UsageLimit>();
        while (r.Read()) result.Add(new(r.GetString(0), r.GetInt32(1), (UsageMetric)r.GetInt32(2), (ReminderLevel)r.GetInt32(3), r.GetBoolean(4), r.GetInt32(5)));
        return result;
    }
    public void SaveLimit(UsageLimit rule)
    {
        rule.Validate();
        using var known = Command("SELECT 1 FROM usage WHERE app_key=$key AND end_ticks>start_ticks LIMIT 1", null, ("$key", rule.AppKey));
        if (known.ExecuteScalar() is null) throw new InvalidOperationException("尚未记录这个应用。请先打开它并在前台使用片刻，再刷新应用列表。");
        Execute("""
            INSERT INTO usage_limits VALUES($key,$m,$metric,$level,$enabled,1)
            ON CONFLICT(app_key) DO UPDATE SET minutes=excluded.minutes,metric=excluded.metric,level=excluded.level,enabled=excluded.enabled,revision=usage_limits.revision+1
            """, null, ("$key", rule.AppKey), ("$m", rule.Minutes), ("$metric", (int)rule.Metric), ("$level", (int)rule.Level), ("$enabled", rule.Enabled));
    }
    public void DeleteLimit(string key)
    {
        using var tx = _connection.BeginTransaction();
        Execute("DELETE FROM usage_limits WHERE app_key=$key", tx, ("$key", key));
        Execute("DELETE FROM limit_days WHERE app_key=$key", tx, ("$key", key)); tx.Commit();
    }
    public void SetLimitEnabled(string key, bool enabled) => Execute("UPDATE usage_limits SET enabled=$enabled,revision=revision+1 WHERE app_key=$key", null, ("$key", key), ("$enabled", enabled));
    public LimitDayState LoadLimitDay(UsageLimit rule, DateOnly day)
    {
        using var cmd = Command("SELECT revision,next_seconds,pending,dismissed FROM limit_days WHERE app_key=$key AND day=$day", null, ("$key", rule.AppKey), ("$day", day.ToString("yyyy-MM-dd")));
        using var r = cmd.ExecuteReader();
        return LimitPolicy.ForDay(rule, day, r.Read() ? new(rule.AppKey, day, r.GetInt32(0), r.GetDouble(1), r.GetBoolean(2), r.GetBoolean(3)) : null);
    }
    public void SaveLimitDay(LimitDayState state) => Execute("""
        INSERT INTO limit_days VALUES($key,$day,$revision,$next,$pending,$dismissed)
        ON CONFLICT(app_key,day) DO UPDATE SET revision=excluded.revision,next_seconds=excluded.next_seconds,pending=excluded.pending,dismissed=excluded.dismissed
        """, null, ("$key", state.AppKey), ("$day", state.Date.ToString("yyyy-MM-dd")), ("$revision", state.Revision), ("$next", state.NextSeconds), ("$pending", state.Pending), ("$dismissed", state.Dismissed));
    public void PruneLimitDays(DateOnly today) => Execute("DELETE FROM limit_days WHERE day<$day", null, ("$day", today.AddDays(-35).ToString("yyyy-MM-dd")));

    public UsageAnalysis Analyze(DateOnly end, int count, TimeZoneInfo zone)
    {
        if (count is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(count));
        var first = end.AddDays(1 - count);
        var (a, _) = ReportBuilder.Bounds(first, zone); var (_, b) = ReportBuilder.Bounds(end, zone);
        var usage = ReadUsage(a, b); var idle = ReadIdle(a, b);
        return new(Enumerable.Range(0, count).Select(i => ReportBuilder.Build(first.AddDays(i), zone, usage, idle)).ToArray());
    }
}
