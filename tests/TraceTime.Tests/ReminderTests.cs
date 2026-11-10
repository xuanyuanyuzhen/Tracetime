using Microsoft.Data.Sqlite;
using TraceTime.Core;
using TraceTime.Storage;

internal static class ReminderTests
{
    private static readonly DateOnly Day = new(2026,11,1);
    private static readonly UsageLimit Rule = new("a",10,UsageMetric.Foreground,ReminderLevel.Medium);
    private static LimitDayState State => LimitPolicy.ForDay(Rule,Day,null);
    private static AppUsage Usage(double foreground, double active = 0) => new(new("a","Example App","C:/private/app.exe"),TimeSpan.FromSeconds(foreground),TimeSpan.FromSeconds(active));
    private static void Check(bool ok) { if (!ok) throw new Exception("Reminder scenario assertion failed"); }
    internal static readonly (string Name,Action Test)[] All = [
        ("删除日期同步清理邮件摘要与临时状态，保留相邻日",()=>WithDb((db,path)=>{
            Seed(db);db.SaveLimit(Rule with {EmailEnabled=true});var rule=db.LoadLimits().Single();
            var notice=EmailNoticePolicy.Due(rule,State,Usage(1200)).Single();db.QueueEmail(notice);
            db.QueueEmail(notice with {Id="neighbor",Day=Day.AddDays(1)});db.SaveLimitDay(LimitPolicy.StartTemporary(State,1200,5,"delete"));
            var (a,b)=ReportBuilder.Bounds(Day,TimeZoneInfo.Utc);db.DeleteRange(a,b,TimeZoneInfo.Utc);
            Check(db.ReadEmails(Day).Count==0&&db.ReadEmails(Day.AddDays(1)).Count==1&&db.LoadLimitDay(rule,Day).TemporaryUntilForeground is null);
        })),
        ("等待确认时发生空闲回扣，不清除已提醒记忆",()=>{
            var state=LimitPolicy.BeginPresentation(State,900,600);
            state=LimitPolicy.Acknowledge(state,100);
            Check(state.Acknowledged&&!LimitPolicy.ShouldPresent(Rule,state,600,1600,false));
            Check(LimitPolicy.ShouldPresent(Rule,state,600,1600,true));
        }),
        ("重新进入超时应用提醒，同次停留及自身窗口去重",()=>{
            var visits = new LimitVisits(); visits.Observe("a",true);
            Check(LimitPolicy.ShouldPresent(Rule,State,600,600,visits.IsNewVisit(Rule,Day)));
            var state=LimitPolicy.Acknowledge(LimitPolicy.BeginPresentation(State,600),600); visits.Presented(Rule,Day);
            Check(!LimitPolicy.ShouldPresent(Rule,state,700,700,visits.IsNewVisit(Rule,Day)));
            Check(!visits.Observe("tracetime",true,true)); visits.Observe("a",true);
            Check(!visits.IsNewVisit(Rule,Day)); visits.Observe("b",true); visits.Observe("a",true);
            Check(LimitPolicy.ShouldPresent(Rule,state,700,700,visits.IsNewVisit(Rule,Day)));
        }),
        ("关闭再次进入和显式今天忽略，跨日重置",()=>{
            var state=LimitPolicy.Acknowledge(State,600);
            Check(!LimitPolicy.ShouldPresent(Rule with {RepeatOnEntry=false},state,600,600,true));
            Check(!LimitPolicy.ShouldPresent(Rule,LimitPolicy.Dismiss(state),900,900,true));
            var tomorrow=LimitPolicy.ForDay(Rule,Day.AddDays(1),LimitPolicy.Dismiss(state));
            Check(!tomorrow.Dismissed && !tomorrow.Acknowledged && tomorrow.TemporaryUntilForeground is null);
        }),
        ("提前临时使用含空闲，未满每日额度也会到期提醒",()=>{
            var state=LimitPolicy.StartTemporary(State,100,5,"one");
            Check(!LimitPolicy.ShouldPresent(Rule,state,200,399,true));
            Check(LimitPolicy.ShouldPresent(Rule,state,200,400,false));
            state=LimitPolicy.Acknowledge(LimitPolicy.BeginPresentation(state,400),200);
            Check(!LimitPolicy.ShouldPresent(Rule,state,200,400,true));
            Check(LimitPolicy.ShouldPresent(Rule,state,600,800,false));
        }),
        ("临时使用后台暂停不消耗，空闲回扣不改变剩余",()=>{
            var rule=Rule with {Metric=UsageMetric.WithoutIdle}; var state=LimitPolicy.StartTemporary(State,800,10,"two");
            foreach (var active in new[]{800d,100d,0d}) Check(!LimitPolicy.ShouldPresent(rule,state,active,1399,true));
            Check(LimitPolicy.ShouldPresent(rule,state,0,1400,false));
            Check(state.TemporaryUntilForeground==1400); // no wall time input: background cannot consume allowance
        }),
        ("临时使用覆盖今日忽略，可取消并恢复每日额度",()=>{
            var state=LimitPolicy.StartTemporary(LimitPolicy.Dismiss(State),1000,5,"three");
            Check(!state.Dismissed && !LimitPolicy.ShouldPresent(Rule,state,1000,1000,true));
            state=LimitPolicy.CancelTemporary(state); Check(LimitPolicy.ShouldPresent(Rule,state,1000,1000,false));
            foreach(var minutes in new[]{0,121}) { try { LimitPolicy.StartTemporary(State,0,minutes,"x"); throw new Exception("accepted invalid minutes"); } catch(ArgumentException) {} }
        }),
        ("暂停会话不呈现，恢复新访问，严重优先且中度待确认保留",()=>{
            var visits=new LimitVisits(); visits.Observe("b",true);
            var severe=Rule with {AppKey="b",Level=ReminderLevel.Severe};
            var report=new DayReport(Day,[Usage(700),new(new("b","B"),TimeSpan.FromSeconds(700),TimeSpan.Zero)],[]);
            LimitDayState Load(UsageLimit r)=>r.AppKey=="a"?LimitPolicy.Begin(State):LimitPolicy.ForDay(r,Day,null);
            Check(LimitDispatch.NextForVisit([Rule,severe],report,Load,visits,false) is null);
            Check(LimitDispatch.NextForVisit([Rule,severe],report,Load,visits,true)?.Rule==severe);
            Check(Load(Rule).Pending); visits.Presented(severe,Day); visits.Observe(null,false); visits.Observe("b",true); Check(visits.IsNewVisit(severe,Day));
        }),
        ("旧数据库迁移保留双时长及今日忽略，新选项默认无邮件",()=>WithDb((db,path)=>{
            Seed(db);
            using(var c=new SqliteConnection($"Data Source={path};Pooling=False")) {
                c.Open();using var cmd=c.CreateCommand();cmd.CommandText="""
                    DROP TABLE usage_limits; DROP TABLE limit_days;
                    CREATE TABLE usage_limits(app_key TEXT PRIMARY KEY,minutes INTEGER,metric INTEGER,level INTEGER,enabled INTEGER,revision INTEGER);
                    CREATE TABLE limit_days(app_key TEXT,day TEXT,revision INTEGER,next_seconds REAL,pending INTEGER,dismissed INTEGER,PRIMARY KEY(app_key,day));
                    INSERT INTO usage_limits VALUES('a',10,0,1,1,7);
                    INSERT INTO limit_days VALUES('a','2026-11-01',7,600,0,1);
                    PRAGMA user_version=2;
                    """;cmd.ExecuteNonQuery();
            }
            using var migrated=new UsageDatabase(path); var rule=migrated.LoadLimits().Single();
            Check(rule.Revision==7&&rule.RepeatOnEntry&&!rule.EmailEnabled&&migrated.LoadLimitDay(rule,Day).Dismissed);
            Check(migrated.GetDay(Day,TimeZoneInfo.Utc).Foreground.TotalMinutes==20);
        })),
        ("临时额度、确认状态和选项重启持久化",()=>WithDb((db,path)=>{
            Seed(db);db.SaveLimit(Rule with {RepeatOnEntry=false,EmailEnabled=true});var rule=db.LoadLimits().Single();
            var state=LimitPolicy.StartTemporary(db.LoadLimitDay(rule,Day),1200,15,"persist");db.SaveLimitDay(state);
            using var reopened=new UsageDatabase(path); Check(reopened.LoadLimitDay(rule,Day)==state&&reopened.LoadLimits().Single()==rule);
            Check(!LimitPolicy.ForDay(rule,Day.AddDays(1),state).TemporaryUntilForeground.HasValue);
        })),
        ("邮件每日与每次临时额度分别去重，重复进入不多发",()=>WithDb((db,path)=>{
            Seed(db);var rule=Rule with {EmailEnabled=true};db.SaveLimit(rule);rule=db.LoadLimits().Single();
            var state=LimitPolicy.StartTemporary(State,600,5,"token");
            var notices=EmailNoticePolicy.Due(rule,state,Usage(1200)).ToArray(); Check(notices.Length==2);
            foreach(var n in notices.Concat(notices))db.QueueEmail(n);
            Check(db.ReadEmails(Day).Count==2);
            Check(!notices[0].Body.Contains("private")&&!notices[0].Body.Contains("app.exe"));
            var next=LimitPolicy.StartTemporary(state,1200,1,"token2");foreach(var n in EmailNoticePolicy.Due(rule,next,Usage(1260)))db.QueueEmail(n);
            Check(db.ReadEmails(Day).Count==3);
        })),
        ("邮件开关、暂停规则和今日忽略抑制外发事件",()=>{
            Check(!EmailNoticePolicy.Due(Rule,State,Usage(1200)).Any());
            Check(!EmailNoticePolicy.Due(Rule with {EmailEnabled=true,Enabled=false},State,Usage(1200)).Any());
            Check(!EmailNoticePolicy.Due(Rule with {EmailEnabled=true},LimitPolicy.Dismiss(State),Usage(1200)).Any());
        }),
        ("邮件中断不自动重试、规则变更取消、跨日不补发",()=>WithDb((db,path)=>{
            Seed(db);db.SaveLimit(Rule with {EmailEnabled=true});var rule=db.LoadLimits().Single();var notice=EmailNoticePolicy.Due(rule,State,Usage(1200)).Single();
            db.QueueEmail(notice);db.SetEmailStatus(notice.Id,"sending","sending");db.RecoverEmailAttempts();Check(db.ReadEmails(Day).Single().Status=="failed");
            db.QueueEmail(notice);Check(db.ReadEmails(Day).Single().Status=="failed");db.RetryFailedEmails(Day);Check(db.ReadEmails(Day).Single().Status=="queued");
            db.SetLimitEnabled("a",false);Check(db.ReadEmails(Day).Single().Status=="cancelled");
            db.SetEmailStatus(notice.Id,"queued","");db.PruneEmails(Day.AddDays(1));Check(db.ReadEmails(Day).Single().Status=="cancelled");
        })),
        ("SMTP配置仅支持STARTTLS，拒绝地址与头部注入",()=>{
            var connection=new EmailConnection("smtp.example.test",587,"from@example.test","to@example.test","user");connection.Validate();
            foreach(var bad in new[]{connection with {Port=465},connection with {Host="https://example.test"},connection with {To="a@example.test\r\nBcc:x@example.test"},connection with {From="Name <a@example.test>"}}) {
                try {bad.Validate();throw new Exception("invalid connection accepted");} catch(ArgumentException){}
            }
        })
    ];
    private static void Seed(UsageDatabase db) { var start=new DateTimeOffset(2026,11,1,8,0,0,TimeSpan.Zero);db.Save(new([new("s","s",new("a","Example App"),start,start.AddMinutes(20))],[])); }
    private static void WithDb(Action<UsageDatabase,string> action)
    {
        var dir=Path.Combine(Path.GetTempPath(),"TraceTime-reminder-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        try {var path=Path.Combine(dir,"usage.db");using var db=new UsageDatabase(path);action(db,path);} finally {Directory.Delete(dir,true);}
    }
}
