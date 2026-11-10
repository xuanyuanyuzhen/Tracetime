using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using TraceTime.Core;

namespace TraceTime.Storage;

public sealed record HistoryArchiveInfo(string Path, DateTimeOffset Start, DateTimeOffset End, string Zone,
    long UsageCount, long IdleCount, long GapCount, string Sha256)
{
    public long Records => UsageCount + IdleCount + GapCount;
}
public sealed record HistoryRestoreResult(string SafetyPath, string? CleanupWarning);

public sealed partial class UsageDatabase
{
    public (long Usage, long Idle, long Gaps) HistoryCounts()
    {
        long Count(string table) { using var c=Command($"SELECT COUNT(*) FROM {table}"); return (long)c.ExecuteScalar()!; }
        return (Count("usage"),Count("idle"),Count("gaps"));
    }
    private static SqliteConnection OpenHistoryRead(string path)
    {
        var c=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=System.IO.Path.GetFullPath(path),Mode=SqliteOpenMode.ReadOnly,Pooling=false }.ToString());
        try { c.Open(); using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA trusted_schema=OFF; PRAGMA query_only=ON; PRAGMA busy_timeout=3000;";cmd.ExecuteNonQuery();return c; }
        catch {c.Dispose();throw;}
    }
    private static SqliteCommand HistoryCommand(SqliteConnection c, SqliteTransaction? tx,string sql,params (string Key,object Value)[] values)
    {
        var cmd=c.CreateCommand();cmd.Transaction=tx;cmd.CommandText=sql;
        foreach(var (key,value) in values)cmd.Parameters.AddWithValue(key,value);return cmd;
    }
    private static (DateTimeOffset Start,DateTimeOffset End)? HistoryBounds(SqliteConnection c,SqliteTransaction tx,TimeZoneInfo zone)
    {
        using var cmd=HistoryCommand(c,tx,"SELECT MIN(start_ticks),MAX(end_ticks) FROM (SELECT start_ticks,end_ticks FROM usage UNION ALL SELECT start_ticks,end_ticks FROM idle UNION ALL SELECT start_ticks,end_ticks FROM gaps) WHERE end_ticks>start_ticks");
        using var r=cmd.ExecuteReader();r.Read();if(r.IsDBNull(0))return null;
        var first=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Stamp(r.GetInt64(0)),zone).DateTime);
        var last=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Stamp(r.GetInt64(1)-1),zone).DateTime);
        return (ReportBuilder.Bounds(first,zone).Start,ReportBuilder.Bounds(last,zone).End);
    }
    public static HistoryArchiveInfo CreateHistoryArchive(string sourcePath,string destination,TimeZoneInfo zone,DateOnly? month=null) =>
        CreateHistoryArchiveCore(sourcePath,destination,zone,month,null);

    // A read transaction pins a consistent WAL snapshot while the independent collector can keep writing.
    private static HistoryArchiveInfo CreateHistoryArchiveCore(string sourcePath,string destination,TimeZoneInfo zone,DateOnly? month,(DateTimeOffset Start,DateTimeOffset End)? safetyRange)
    {
        destination=System.IO.Path.GetFullPath(destination); sourcePath=System.IO.Path.GetFullPath(sourcePath);
        if(!destination.EndsWith(".ttarchive",StringComparison.OrdinalIgnoreCase)||string.Equals(sourcePath,destination,StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请选择独立的 .ttarchive 历史归档文件。");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
        var temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var source=OpenHistoryRead(sourcePath))
            using(var snapshot=source.BeginTransaction(deferred:true))
            using(var target=new UsageDatabase(temporary))
            {
                var bounds=HistoryBounds(source,snapshot,zone);
                (DateTimeOffset Start,DateTimeOffset End) range;
                if(month is { } selected) {
                    var first=new DateOnly(selected.Year,selected.Month,1); range=(ReportBuilder.Bounds(first,zone).Start,ReportBuilder.Bounds(first.AddMonths(1),zone).Start);
                }
                else if(safetyRange is { } safety)range=(bounds.HasValue&&bounds.Value.Start<safety.Start?bounds.Value.Start:safety.Start,bounds.HasValue&&bounds.Value.End>safety.End?bounds.Value.End:safety.End);
                else range=bounds??throw new InvalidOperationException("尚无历史记录可备份。");
                using(var tx=target._connection.BeginTransaction())
                {
                    CopyHistory(source,snapshot,target,tx,range.Start,range.End,false);
                    target.Execute("CREATE TABLE history_archive (version INTEGER NOT NULL,start_ticks INTEGER NOT NULL,end_ticks INTEGER NOT NULL,zone TEXT NOT NULL);",tx);
                    target.Execute("INSERT INTO history_archive VALUES(1,$a,$b,$zone)",tx,("$a",range.Start.UtcTicks),("$b",range.End.UtcTicks),("$zone",zone.Id));
                    tx.Commit();
                }
                target.Execute("PRAGMA wal_checkpoint(TRUNCATE); PRAGMA journal_mode=DELETE;");
            }
            var verified=InspectHistoryArchive(temporary);
            if(verified.Records==0&&safetyRange is null)throw new InvalidOperationException("所选月份没有可归档的记录。");
            File.Move(temporary,destination,true);
            return verified with {Path=destination};
        }
        finally
        {
            // Only uniquely named staging files beside the explicitly chosen destination are removed.
            foreach(var suffix in new[]{"","-wal","-shm"})if(File.Exists(temporary+suffix))File.Delete(temporary+suffix);
        }
    }
    private static void CopyHistory(SqliteConnection source,SqliteTransaction snapshot,UsageDatabase target,SqliteTransaction tx,DateTimeOffset a,DateTimeOffset b,bool freshIds)
    {
        using(var cmd=HistoryCommand(source,snapshot,"SELECT id,scope,app_key,name,path,MAX(start_ticks,$a),MIN(end_ticks,$b),threshold_ms FROM usage WHERE end_ticks>$a AND start_ticks<$b AND end_ticks>start_ticks",("$a",a.UtcTicks),("$b",b.UtcTicks)))
        using(var r=cmd.ExecuteReader())while(r.Read())target.InsertUsage(new(freshIds?Id():r.GetString(0),r.GetString(1),new(r.GetString(2),r.GetString(3),r.IsDBNull(4)?null:r.GetString(4)),Stamp(r.GetInt64(5)),Stamp(r.GetInt64(6)),r.GetInt64(7)),tx);
        using(var cmd=HistoryCommand(source,snapshot,"SELECT id,scope,MAX(start_ticks,$a),MIN(end_ticks,$b) FROM idle WHERE end_ticks>$a AND start_ticks<$b AND end_ticks>start_ticks",("$a",a.UtcTicks),("$b",b.UtcTicks)))
        using(var r=cmd.ExecuteReader())while(r.Read())target.InsertIdle(new(freshIds?Id():r.GetString(0),r.GetString(1),Stamp(r.GetInt64(2)),Stamp(r.GetInt64(3))),tx);
        using(var cmd=HistoryCommand(source,snapshot,"SELECT id,MAX(start_ticks,$a),MIN(end_ticks,$b),reason FROM gaps WHERE end_ticks>$a AND start_ticks<$b AND end_ticks>start_ticks",("$a",a.UtcTicks),("$b",b.UtcTicks)))
        using(var r=cmd.ExecuteReader())while(r.Read())target.InsertGap(new(freshIds?Id():r.GetString(0),Stamp(r.GetInt64(1)),Stamp(r.GetInt64(2)),r.GetString(3)),tx);
    }
    public static HistoryArchiveInfo InspectHistoryArchive(string path)
    {
        path=System.IO.Path.GetFullPath(path);
        if(!File.Exists(path)||new FileInfo(path).Length>2L*1024*1024*1024)throw new InvalidOperationException("文件不存在或超过2GiB，无法作为本版本历史归档读取。");
        using var c=OpenHistoryRead(path);using var tx=c.BeginTransaction(deferred:true);
        using(var mode=HistoryCommand(c,tx,"PRAGMA journal_mode"))if(mode.ExecuteScalar() as string!="delete")throw new InvalidOperationException("归档必须是完整的单文件快照，不能依赖外部日志。");
        using(var schema=HistoryCommand(c,tx,"SELECT COUNT(*) FROM sqlite_master WHERE type IN ('trigger','view') OR upper(sql) LIKE '%VIRTUAL TABLE%'"))
            if((long)schema.ExecuteScalar()! != 0)throw new InvalidOperationException("归档包含不支持的数据库对象。");
        foreach(var table in new[]{"history_archive","usage","idle","gaps"})
        {
            using var tableCheck=HistoryCommand(c,tx,"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name",("$name",table));
            if((long)tableCheck.ExecuteScalar()! !=1)throw new InvalidOperationException("不是受支持的 TraceTime 历史归档。");
        }
        using(var check=HistoryCommand(c,tx,"PRAGMA quick_check"))if(check.ExecuteScalar() as string!="ok")throw new InvalidOperationException("归档完整性检查未通过。");
        long start,end;string zone;
        using(var metadata=HistoryCommand(c,tx,"SELECT version,start_ticks,end_ticks,zone FROM history_archive"))
        using(var r=metadata.ExecuteReader())
        {
            if(!r.Read()||r.GetInt32(0)!=1)throw new InvalidOperationException("不支持此归档版本。");
            start=r.GetInt64(1);end=r.GetInt64(2);zone=r.GetString(3);
            if(r.Read()||start<TimeSpan.TicksPerDay*2||end<=start||end>DateTimeOffset.MaxValue.UtcTicks-TimeSpan.TicksPerDay*2||zone.Length>256)throw new InvalidOperationException("归档日期范围无效。");
        }
        long Count(string table)
        {
            using(var invalid=HistoryCommand(c,tx,$"SELECT COUNT(*) FROM {table} WHERE start_ticks<$a OR end_ticks>$b OR end_ticks<=start_ticks OR start_ticks IS NULL OR end_ticks IS NULL",("$a",start),("$b",end)))
                if((long)invalid.ExecuteScalar()!>0)throw new InvalidOperationException("归档中包含范围外或无效的时间片。");
            using var cmd=HistoryCommand(c,tx,$"SELECT COUNT(*) FROM {table}");var count=(long)cmd.ExecuteScalar()!;
            if(count>5_000_000)throw new InvalidOperationException("归档记录数量超过此版本的恢复上限。");return count;
        }
        var usage=Count("usage");var idle=Count("idle");var gaps=Count("gaps");
        using(var bad=HistoryCommand(c,tx,"SELECT COUNT(*) FROM usage WHERE id IS NULL OR scope IS NULL OR app_key IS NULL OR name IS NULL OR length(name)>4096 OR length(app_key)>32768 OR length(path)>32768 OR threshold_ms IS NULL OR threshold_ms<0"))
            if((long)bad.ExecuteScalar()!>0)throw new InvalidOperationException("归档应用字段无效。");
        using var stream=File.OpenRead(path);var hash=Convert.ToHexString(SHA256.HashData(stream));
        return new(path,Stamp(start),Stamp(end),zone,usage,idle,gaps,hash);
    }
    // Call with the collector suspended. Preview hash binds the restore to the reviewed file.
    public HistoryRestoreResult RestoreHistoryArchive(string path,string previewHash,string safetyPath,DateOnly today,TimeZoneInfo zone) =>
        RestoreHistoryArchive(path,previewHash,safetyPath,today,zone,File.Delete);

    internal HistoryRestoreResult RestoreHistoryArchive(string path,string previewHash,string safetyPath,DateOnly today,TimeZoneInfo zone,Action<string> cleanupStaging)
    {
        if(string.Equals(System.IO.Path.GetFullPath(path),System.IO.Path.GetFullPath(safetyPath),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("恢复前备份不能覆盖选中的归档。");
        var staged=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(FilePath)!,"restore-"+Guid.NewGuid().ToString("N")+".ttarchive");
        string? cleanupWarning=null;
        Exception? failure=null;
        try
        {
            File.Copy(path,staged);var archive=InspectHistoryArchive(staged);
            if(!string.Equals(archive.Sha256,previewHash,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("归档在预览后发生变化，请重新选择并确认。");
            var safety=CreateHistoryArchiveCore(FilePath,safetyPath,zone,null,(archive.Start,archive.End));
            using var source=OpenHistoryRead(staged);using var snapshot=source.BeginTransaction(deferred:true);
            using var tx=_connection.BeginTransaction();
            DeleteRangeCore(archive.Start,archive.End,zone,tx);
            CopyHistory(source,snapshot,this,tx,archive.Start,archive.End,true);
            Execute("INSERT INTO settings VALUES('reminderQuietDay',$day) ON CONFLICT(key) DO UPDATE SET value=excluded.value",tx,("$day",today.ToString("yyyy-MM-dd")));
            Execute("UPDATE email_notices SET status='cancelled',detail='恢复历史后今日提醒已静默' WHERE status IN ('queued','failed','sending')",tx);
            tx.Commit();HistoryRevision++;
        }
        catch(Exception ex) {failure=ex;throw;}
        finally
        {
            try {if(File.Exists(staged))cleanupStaging(staged);}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
            {
                // Cleanup must never turn a committed replacement into a reported restore failure.
                cleanupWarning="临时恢复副本未能自动移除，可稍后在数据目录清理："+staged;
                if(failure is not null)failure.Data["HistoryArchiveCleanup"]=cleanupWarning;
            }
        }
        return new(System.IO.Path.GetFullPath(safetyPath),cleanupWarning);
    }
}
