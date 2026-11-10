using Microsoft.Data.Sqlite;
using TraceTime.App;
using TraceTime.Core;
using TraceTime.Storage;

internal static class HistoryTests
{
    private static readonly DateOnly Day=new(2026,11,2);
    private static readonly DateTimeOffset Start=new(2026,10,31,23,58,0,TimeSpan.Zero);
    internal static readonly (string Name,Action Test)[] All = [
        ("恢复提交后临时副本被占用仍返回成功并保留清理诊断",()=>WithDb((db,path,dir)=>{
            Seed(db);var archive=UsageDatabase.CreateHistoryArchive(path,Path.Combine(dir,"cleanup.ttarchive"),TimeZoneInfo.Utc);
            db.Save(new([new("extra","extra",new("b","B"),Start.AddHours(2),Start.AddHours(3))],[]));
            string? leftover=null;
            var result=db.RestoreHistoryArchive(archive.Path,archive.Sha256,Path.Combine(dir,"before-cleanup.ttarchive"),Day,TimeZoneInfo.Utc,staged=>{
                leftover=staged;
                if(!OperatingSystem.IsWindows())throw new IOException("Injected sharing violation");
                using var locked=new FileStream(staged,FileMode.Open,FileAccess.Read,FileShare.Read);
                File.Delete(staged); // Actual Windows sharing violation, only inside this isolated fixture.
            });
            Near(db.GetDay(new(2026,11,1),TimeZoneInfo.Utc).Foreground.TotalMinutes,3);
            Check(db.IsReminderQuiet(Day)&&File.Exists(result.SafetyPath)&&result.CleanupWarning is not null&&File.Exists(leftover));
        })),
        ("安全备份失败时不开始恢复",()=>WithDb((db,path,dir)=>{
            Seed(db);var archive=UsageDatabase.CreateHistoryArchive(path,Path.Combine(dir,"safe-source.ttarchive"),TimeZoneInfo.Utc);
            db.Save(new([new("new","new",new("b","B"),Start.AddHours(2),Start.AddHours(3))],[]));
            Fails(()=>db.RestoreHistoryArchive(archive.Path,archive.Sha256,Path.Combine(dir,"invalid-backup.db"),Day,TimeZoneInfo.Utc));
            Near(db.GetDay(new(2026,11,1),TimeZoneInfo.Utc).Foreground.TotalMinutes,63);Check(!db.IsReminderQuiet(Day));
        })),
        ("夏令时月归档保留真实23小时及原始时区",()=>WithDb((db,path,dir)=>{
            var zone=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");var (a,b)=ReportBuilder.Bounds(new(2026,3,8),zone);
            db.Save(new([new("dst","dst",new("a","A"),a,b)],[]));var archive=UsageDatabase.CreateHistoryArchive(path,Path.Combine(dir,"dst.ttarchive"),zone,new(2026,3,1));
            Check(archive.Zone==zone.Id);db.RestoreHistoryArchive(archive.Path,archive.Sha256,Path.Combine(dir,"before-dst.ttarchive"),Day,zone);
            Near(db.GetDay(new(2026,3,8),zone).Foreground.TotalHours,23);
        })),
        ("应用名称搜索忽略大小写和边缘空格",()=>{
            Check(ApplicationQuery.Matches("Visual Studio Code"," code ")&&ApplicationQuery.Matches("记事本","事")&&ApplicationQuery.Matches("A","")&&!ApplicationQuery.Matches("Edge","chrome"));
        }),
        ("有记录日期跳转跨空白年份与午夜，空闲仍算记录",()=>WithDb((db,path,dir)=>{
            Seed(db);var old=Start.AddYears(-2);db.Save(new([new("old","old",new("a","A"),old,old.AddMinutes(1))],[]));
            Check(db.FindRecordedDay(new(2026,11,1),-1,TimeZoneInfo.Utc)==new DateOnly(2026,10,31));
            Check(db.FindRecordedDay(new(2026,10,31),1,TimeZoneInfo.Utc)==new DateOnly(2026,11,1));
            Check(db.FindRecordedDay(new(2026,10,30),-1,TimeZoneInfo.Utc)==new DateOnly(2024,10,31));
            Check(db.FindRecordedDay(new(2026,11,1),1,TimeZoneInfo.Utc) is null);
        })),
        ("今日全局静默重启保留，恢复不重置每应用状态",()=>WithDb((db,path,dir)=>{
            Seed(db);db.SaveLimit(new("a",1,UsageMetric.Foreground,ReminderLevel.Medium));var rule=db.LoadLimits().Single();
            var state=LimitPolicy.Dismiss(db.LoadLimitDay(rule,Day));db.SaveLimitDay(state);db.SetReminderQuiet(Day,true);
            using(var other=new UsageDatabase(path))Check(other.IsReminderQuiet(Day)&&!other.IsReminderQuiet(Day.AddDays(1)));
            db.SetReminderQuiet(Day,false);Check(!db.IsReminderQuiet(Day)&&db.LoadLimitDay(rule,Day)==state&&db.LoadLimits().Single()==rule);
        })),
        ("常用临时分钟兼容旧偏好并限制有效范围",()=>WithDb((db,path,dir)=>{
            var file=Path.Combine(dir,"views.json");File.WriteAllText(file,"{\"DayChart\":\"ring\"}");Check(ViewPreferences.Load(file).TemporaryMinutes==10);
            new ViewPreferences(true,"timeline",25).Save(file);Check(ViewPreferences.Load(file)==new ViewPreferences(true,"timeline",25));
            new ViewPreferences(false,"bar",200).Save(file);Check(ViewPreferences.Load(file).TemporaryMinutes==10);
        })),
        ("运行中WAL记录一致性归档，原记录保留且无配置",()=>WithDb((db,path,dir)=>{
            Seed(db);db.SaveSettings(new(2));db.SaveLimit(new("a",1,UsageMetric.Foreground,ReminderLevel.Light,EmailEnabled:true));db.SetReminderQuiet(Day,true);
            var archive=UsageDatabase.CreateHistoryArchive(path,Path.Combine(dir,"all.ttarchive"),TimeZoneInfo.Utc);
            Check(archive.UsageCount==1&&archive.IdleCount==1&&archive.GapCount==1);
            using var restored=new UsageDatabase(archive.Path);Check(restored.LoadSettings().IdleMinutes==5&&restored.LoadLimits().Count==0&&!restored.IsReminderQuiet(Day));
            Near(db.GetDay(new(2026,11,1),TimeZoneInfo.Utc).Foreground.TotalMinutes,3);
            Near(restored.GetDay(new(2026,10,31),TimeZoneInfo.Utc).WithoutIdle.TotalMinutes,1);
        })),
        ("月归档裁剪跨月区间，恢复多次不重复相邻片段",()=>WithDb((db,path,dir)=>{
            Seed(db);var october=UsageDatabase.CreateHistoryArchive(path,Path.Combine(dir,"oct.ttarchive"),TimeZoneInfo.Utc,new(2026,10,1));
            var november=UsageDatabase.CreateHistoryArchive(path,Path.Combine(dir,"nov.ttarchive"),TimeZoneInfo.Utc,new(2026,11,1));
            db.RestoreHistoryArchive(october.Path,october.Sha256,Path.Combine(dir,"before1.ttarchive"),Day,TimeZoneInfo.Utc);
            db.RestoreHistoryArchive(november.Path,november.Sha256,Path.Combine(dir,"before2.ttarchive"),Day,TimeZoneInfo.Utc);
            db.RestoreHistoryArchive(november.Path,november.Sha256,Path.Combine(dir,"before3.ttarchive"),Day,TimeZoneInfo.Utc);
            Near(db.GetDay(new(2026,10,31),TimeZoneInfo.Utc).Foreground.TotalMinutes,2);
            Near(db.GetDay(new(2026,11,1),TimeZoneInfo.Utc).Foreground.TotalMinutes,3);
            Near(db.GetDay(new(2026,11,1),TimeZoneInfo.Utc).WithoutIdle.TotalMinutes,1);
        })),
        ("恢复先备份当前数据，保持规则设置并静默今日提醒",()=>WithDb((db,path,dir)=>{
            Seed(db);var archive=UsageDatabase.CreateHistoryArchive(path,Path.Combine(dir,"saved.ttarchive"),TimeZoneInfo.Utc);
            db.Save(new([new("later","later",new("b","B"),Start.AddHours(2),Start.AddHours(3))],[]));db.SaveLimit(new("a",2,UsageMetric.Foreground,ReminderLevel.Severe));db.SaveSettings(new(3));
            var before=Path.Combine(dir,"before.ttarchive");db.RestoreHistoryArchive(archive.Path,archive.Sha256,before,Day,TimeZoneInfo.Utc);
            Near(db.GetDay(new(2026,11,1),TimeZoneInfo.Utc).Foreground.TotalMinutes,3);Check(db.IsReminderQuiet(Day)&&db.LoadLimits().Single().Minutes==2&&db.LoadSettings().IdleMinutes==3);
            var safety=UsageDatabase.InspectHistoryArchive(before);db.RestoreHistoryArchive(before,safety.Sha256,Path.Combine(dir,"before-undo.ttarchive"),Day,TimeZoneInfo.Utc);
            Near(db.GetDay(new(2026,11,1),TimeZoneInfo.Utc).Foreground.TotalMinutes,63);
        })),
        ("空库恢复也能用恢复前备份撤回",()=>WithDb((db,path,dir)=>{
            var origin=Path.Combine(dir,"origin.db");using(var donor=new UsageDatabase(origin))Seed(donor);
            var archive=UsageDatabase.CreateHistoryArchive(origin,Path.Combine(dir,"source.ttarchive"),TimeZoneInfo.Utc);
            var before=Path.Combine(dir,"empty-before.ttarchive");db.RestoreHistoryArchive(archive.Path,archive.Sha256,before,Day,TimeZoneInfo.Utc);Check(db.HistoryCounts().Usage>0);
            var safety=UsageDatabase.InspectHistoryArchive(before);Check(safety.Records==0);db.RestoreHistoryArchive(before,safety.Sha256,Path.Combine(dir,"undo.ttarchive"),Day,TimeZoneInfo.Utc);Check(db.HistoryCounts().Usage==0);
        })),
        ("预览后文件变化拒绝恢复且不触碰原记录",()=>WithDb((db,path,dir)=>{
            Seed(db);var archive=UsageDatabase.CreateHistoryArchive(path,Path.Combine(dir,"changed.ttarchive"),TimeZoneInfo.Utc);
            Sql(archive.Path,"UPDATE usage SET name='changed'");
            Fails(()=>db.RestoreHistoryArchive(archive.Path,archive.Sha256,Path.Combine(dir,"unused.ttarchive"),Day,TimeZoneInfo.Utc));
            Check(!File.Exists(Path.Combine(dir,"unused.ttarchive"))&&db.RecordedApps().Single().Name=="A");
        })),
        ("恢复中途失败整笔回滚，安全备份仍可读取",()=>WithDb((db,path,dir)=>{
            Seed(db);var archive=UsageDatabase.CreateHistoryArchive(path,Path.Combine(dir,"rollback.ttarchive"),TimeZoneInfo.Utc);
            Sql(path,"CREATE TRIGGER fail_restore BEFORE INSERT ON usage BEGIN SELECT RAISE(ABORT,'injected restore failure'); END");
            Fails(()=>db.RestoreHistoryArchive(archive.Path,archive.Sha256,Path.Combine(dir,"before-failure.ttarchive"),Day,TimeZoneInfo.Utc));
            Near(db.GetDay(new(2026,11,1),TimeZoneInfo.Utc).Foreground.TotalMinutes,3);Check(!db.IsReminderQuiet(Day));
            Check(UsageDatabase.InspectHistoryArchive(Path.Combine(dir,"before-failure.ttarchive")).UsageCount==1);
        })),
        ("无效归档和空月不覆盖已有文件",()=>WithDb((db,path,dir)=>{
            Seed(db);var output=Path.Combine(dir,"keep.ttarchive");File.WriteAllText(output,"keep");
            Fails(()=>UsageDatabase.CreateHistoryArchive(path,output,TimeZoneInfo.Utc,new(2025,1,1)));Check(File.ReadAllText(output)=="keep");
            Fails(()=>UsageDatabase.InspectHistoryArchive(output));Fails(()=>UsageDatabase.InspectHistoryArchive(path));
        })),
        ("归档范围外时间片与不支持版本拒绝恢复",()=>WithDb((db,path,dir)=>{
            Seed(db);var archive=UsageDatabase.CreateHistoryArchive(path,Path.Combine(dir,"invalid.ttarchive"),TimeZoneInfo.Utc);
            Sql(archive.Path,"UPDATE history_archive SET version=99");Fails(()=>UsageDatabase.InspectHistoryArchive(archive.Path));
            Sql(archive.Path,"UPDATE history_archive SET version=1; UPDATE usage SET start_ticks=0");Fails(()=>UsageDatabase.InspectHistoryArchive(archive.Path));
        }))
    ];
    private static void Seed(UsageDatabase db)
    {
        db.Save(new([new("s","scope",new("a","A","C:/Apps/A.exe"),Start,Start.AddMinutes(5))],[new("i","scope",Start.AddMinutes(1),Start.AddMinutes(4))]));
        db.SaveGap(new("g",Start.AddMinutes(10),Start.AddMinutes(20),"已暂停"));
    }
    private static void Sql(string path,string sql){using var c=new SqliteConnection($"Data Source={path};Pooling=False");c.Open();using var cmd=c.CreateCommand();cmd.CommandText=sql;cmd.ExecuteNonQuery();}
    private static void Check(bool ok){if(!ok)throw new Exception("History scenario assertion failed");}
    private static void Near(double actual,double expected){Check(Math.Abs(actual-expected)<0.001);}
    private static void Fails(Action action){try{action();}catch(Exception ex)when(ex is ArgumentException or InvalidOperationException or SqliteException or IOException){return;}throw new Exception("Expected validation failure");}
    private static void WithDb(Action<UsageDatabase,string,string> action)
    {
        var dir=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"TraceTime-history-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(dir);
        try{var path=Path.Combine(dir,"usage.db");using var db=new UsageDatabase(path);action(db,path,dir);}
        finally {if(dir.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))Directory.Delete(dir,true);}
    }
}
