using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using TraceTime.Core;

namespace TraceTime.Storage;

public sealed class UsageDatabase : ITrackingSink, IDisposable
{
    private readonly SqliteConnection _connection;
    public string FilePath { get; }

    public UsageDatabase(string path)
    {
        FilePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        _connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = FilePath, Pooling = false }.ToString());
        _connection.Open();
        Execute("PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL; PRAGMA busy_timeout=3000;");
        Execute("""
            CREATE TABLE IF NOT EXISTS usage (
                id TEXT PRIMARY KEY, scope TEXT NOT NULL, app_key TEXT NOT NULL, name TEXT NOT NULL,
                path TEXT, start_ticks INTEGER NOT NULL, end_ticks INTEGER NOT NULL, threshold_ms INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS usage_time ON usage(start_ticks, end_ticks);
            CREATE TABLE IF NOT EXISTS idle (
                id TEXT PRIMARY KEY, scope TEXT NOT NULL, start_ticks INTEGER NOT NULL, end_ticks INTEGER NOT NULL);
            CREATE INDEX IF NOT EXISTS idle_time ON idle(start_ticks, end_ticks);
            CREATE TABLE IF NOT EXISTS gaps (
                id TEXT PRIMARY KEY, start_ticks INTEGER NOT NULL, end_ticks INTEGER NOT NULL, reason TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            PRAGMA user_version=1;
            """);
    }

    public void Save(ChangeBatch changes)
    {
        using var tx = _connection.BeginTransaction();
        foreach (var s in changes.Usage) InsertUsage(s, tx);
        foreach (var s in changes.Idle) InsertIdle(s, tx);
        tx.Commit();
    }

    private void InsertUsage(UsageSegment s, SqliteTransaction tx) => Execute("""
        INSERT INTO usage VALUES($id,$scope,$key,$name,$path,$a,$b,$threshold)
        ON CONFLICT(id) DO UPDATE SET end_ticks=excluded.end_ticks;
        """, tx, ("$id", s.Id), ("$scope", s.ScopeId), ("$key", s.App.Key), ("$name", s.App.Name),
        ("$path", s.App.ExecutablePath), ("$a", s.Start.UtcTicks), ("$b", s.End.UtcTicks), ("$threshold", s.ThresholdMs));

    private void InsertIdle(IdleInterval s, SqliteTransaction tx) => Execute("""
        INSERT INTO idle VALUES($id,$scope,$a,$b) ON CONFLICT(id) DO UPDATE SET end_ticks=excluded.end_ticks;
        """, tx, ("$id", s.Id), ("$scope", s.ScopeId), ("$a", s.Start.UtcTicks), ("$b", s.End.UtcTicks));

    public void SaveGap(GapInterval gap)
    {
        if (gap.End <= gap.Start) return;
        Execute("INSERT INTO gaps VALUES($id,$a,$b,$reason) ON CONFLICT(id) DO UPDATE SET end_ticks=excluded.end_ticks;", null,
            ("$id", gap.Id), ("$a", gap.Start.UtcTicks), ("$b", gap.End.UtcTicks), ("$reason", gap.Reason));
    }

    public DayReport GetDay(DateOnly date, TimeZoneInfo zone)
    {
        var (a, b) = ReportBuilder.Bounds(date, zone);
        return ReportBuilder.Build(date, zone, ReadUsage(a, b), ReadIdle(a, b), ReadGaps(a, b));
    }

    public List<UsageSegment> ReadUsage(DateTimeOffset a, DateTimeOffset b)
    {
        using var cmd = Command("SELECT id,scope,app_key,name,path,start_ticks,end_ticks,threshold_ms FROM usage WHERE end_ticks>$a AND start_ticks<$b ORDER BY start_ticks", null, ("$a", a.UtcTicks), ("$b", b.UtcTicks));
        using var r = cmd.ExecuteReader();
        var rows = new List<UsageSegment>();
        while (r.Read()) rows.Add(new(r.GetString(0), r.GetString(1), new(r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4)),
            Stamp(r.GetInt64(5)), Stamp(r.GetInt64(6)), r.GetInt64(7)));
        return rows;
    }

    public List<IdleInterval> ReadIdle(DateTimeOffset a, DateTimeOffset b)
    {
        using var cmd = Command("SELECT id,scope,start_ticks,end_ticks FROM idle WHERE end_ticks>$a AND start_ticks<$b", null, ("$a", a.UtcTicks), ("$b", b.UtcTicks));
        using var r = cmd.ExecuteReader();
        var rows = new List<IdleInterval>();
        while (r.Read()) rows.Add(new(r.GetString(0), r.GetString(1), Stamp(r.GetInt64(2)), Stamp(r.GetInt64(3))));
        return rows;
    }

    public List<GapInterval> ReadGaps(DateTimeOffset a, DateTimeOffset b)
    {
        using var cmd = Command("SELECT id,start_ticks,end_ticks,reason FROM gaps WHERE end_ticks>$a AND start_ticks<$b", null, ("$a", a.UtcTicks), ("$b", b.UtcTicks));
        using var r = cmd.ExecuteReader();
        var rows = new List<GapInterval>();
        while (r.Read()) rows.Add(new(r.GetString(0), Stamp(r.GetInt64(1)), Stamp(r.GetInt64(2)), r.GetString(3)));
        return rows;
    }

    public AppSettings LoadSettings()
    {
        using var cmd = Command("SELECT value FROM settings WHERE key='idleMinutes'");
        return int.TryParse(cmd.ExecuteScalar() as string, out var minutes) && minutes is >= 1 and <= 60 ? new(minutes) : new();
    }

    public void SaveSettings(AppSettings settings)
    {
        if (settings.IdleMinutes is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(settings));
        Execute("INSERT INTO settings VALUES('idleMinutes',$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value", null,
            ("$v", settings.IdleMinutes.ToString(CultureInfo.InvariantCulture)));
    }

    // Clip crossing segments instead of erasing the neighboring day's history.
    public void DeleteRange(DateTimeOffset a, DateTimeOffset b)
    {
        if (b <= a) throw new ArgumentException("结束日期须晚于开始日期。");
        var usage = ReadUsage(a, b); var idle = ReadIdle(a, b); var gaps = ReadGaps(a, b);
        using var tx = _connection.BeginTransaction();
        foreach (var table in new[] { "usage", "idle", "gaps" })
            Execute($"DELETE FROM {table} WHERE end_ticks>$a AND start_ticks<$b", tx, ("$a", a.UtcTicks), ("$b", b.UtcTicks));
        foreach (var s in usage)
        {
            if (s.Start < a) InsertUsage(s with { Id = Id(), End = a }, tx);
            if (s.End > b) InsertUsage(s with { Id = Id(), Start = b }, tx);
        }
        foreach (var s in idle)
        {
            if (s.Start < a) InsertIdle(s with { Id = Id(), End = a }, tx);
            if (s.End > b) InsertIdle(s with { Id = Id(), Start = b }, tx);
        }
        foreach (var s in gaps)
        {
            if (s.Start < a) InsertGap(s with { Id = Id(), End = a }, tx);
            if (s.End > b) InsertGap(s with { Id = Id(), Start = b }, tx);
        }
        tx.Commit();
    }

    private void InsertGap(GapInterval g, SqliteTransaction tx) => Execute("INSERT INTO gaps VALUES($id,$a,$b,$r)", tx,
        ("$id", g.Id), ("$a", g.Start.UtcTicks), ("$b", g.End.UtcTicks), ("$r", g.Reason));

    public void ExportCsv(string path, DateOnly first, DateOnly last, TimeZoneInfo zone)
    {
        if (last < first || last.DayNumber - first.DayNumber > 3660) throw new ArgumentException("请选择不超过十年的有效日期范围。");
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("日期,应用,前台停留秒数,扣除空闲后秒数");
        for (var date = first; date <= last; date = date.AddDays(1))
            foreach (var row in GetDay(date, zone).Apps)
                writer.WriteLine($"{date:yyyy-MM-dd},{Csv(row.App.Name)},{row.Foreground.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)},{row.WithoutIdle.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture)}");
    }

    public static string Csv(string text)
    {
        if (text.Length > 0 && "=+-@\t\r\n".Contains(text[0])) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    private static string Id() => Guid.NewGuid().ToString("N");
    private static DateTimeOffset Stamp(long ticks) => new(ticks, TimeSpan.Zero);
    private SqliteCommand Command(string sql, SqliteTransaction? tx = null, params (string Key, object? Value)[] args)
    {
        var cmd = _connection.CreateCommand(); cmd.CommandText = sql; cmd.Transaction = tx;
        foreach (var (key, value) in args) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return cmd;
    }
    private void Execute(string sql, SqliteTransaction? tx = null, params (string Key, object? Value)[] args)
    { using var cmd = Command(sql, tx, args); cmd.ExecuteNonQuery(); }
    public void Dispose() => _connection.Dispose();
}
