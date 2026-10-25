using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using TraceTime.Core;
using TraceTime.Storage;

internal static class FeatureTests
{
    internal static readonly (string Name, Action Test)[] All = [
        ("额度在阈值处触发且区分两种口径", () => {
            var rule = new UsageLimit("a", 10, UsageMetric.Foreground, ReminderLevel.Light);
            var state = LimitPolicy.ForDay(rule, new(2026,10,25), null);
            Assert(!LimitPolicy.IsDue(rule,state,599.9) && LimitPolicy.IsDue(rule,state,600));
            var usage = new AppUsage(new("a","A"),TimeSpan.FromMinutes(10),TimeSpan.FromMinutes(3));
            Assert(LimitPolicy.Seconds(usage,UsageMetric.WithoutIdle)==180 && LimitPolicy.Seconds(usage,UsageMetric.Foreground)==600);
        }),
        ("忽略当天不重复、隔天重新计算", () => {
            var rule = new UsageLimit("a",1,UsageMetric.Foreground,ReminderLevel.Medium);
            var state = LimitPolicy.Dismiss(LimitPolicy.ForDay(rule,new(2026,10,25),null));
            Assert(!LimitPolicy.IsDue(rule,state,10000));
            var next = LimitPolicy.ForDay(rule,new(2026,10,26),state);
            Assert(!next.Dismissed && LimitPolicy.IsDue(rule,next,60));
        }),
        ("延后以继续使用十分钟计数，空闲回扣不会提前提醒", () => {
            var rule = new UsageLimit("a",1,UsageMetric.WithoutIdle,ReminderLevel.Severe);
            var snoozed = LimitPolicy.Snooze(LimitPolicy.ForDay(rule,new(2026,10,25),null),90);
            Assert(snoozed.NextSeconds==690 && !LimitPolicy.IsDue(rule,snoozed,60) && !LimitPolicy.IsDue(rule,snoozed,689) && LimitPolicy.IsDue(rule,snoozed,690));
        }),
        ("未确认提醒保留、暂停不触发、规则修订重置", () => {
            var rule = new UsageLimit("a",1,UsageMetric.Foreground,ReminderLevel.Medium);
            var pending = LimitPolicy.Begin(LimitPolicy.ForDay(rule,new(2026,10,25),null));
            Assert(LimitPolicy.IsDue(rule,pending,59) && !LimitPolicy.IsDue(rule with {Enabled=false},pending,100));
            var revised = LimitPolicy.ForDay(rule with {Minutes=20,Revision=2},new(2026,10,25),pending);
            Assert(!revised.Pending && revised.NextSeconds==1200);
        }),
        ("只允许已有真实时长记录的应用设置额度", () => WithDb((db,path) => {
            Throws<InvalidOperationException>(()=>db.SaveLimit(new("missing",60,UsageMetric.Foreground,ReminderLevel.Light)));
            Seed(db); Assert(db.RecordedApps().Count==1);
            db.SaveLimit(new("a",60,UsageMetric.Foreground,ReminderLevel.Light));
            Assert(db.LoadLimits().Single().Minutes==60);
            Throws<ArgumentException>(()=>db.SaveLimit(new("a",0,UsageMetric.Foreground,ReminderLevel.Light)));
        })),
        ("额度与未确认状态重启持久化、修改隔离历史", () => WithDb((db,path) => {
            Seed(db); db.SaveLimit(new("a",60,UsageMetric.Foreground,ReminderLevel.Severe));
            var rule = db.LoadLimits().Single(); var date = new DateOnly(2026,10,25);
            var state=LimitPolicy.Begin(db.LoadLimitDay(rule,date)); db.SaveLimitDay(state);
            using (var reopen=new UsageDatabase(path)) { Assert(reopen.LoadLimitDay(reopen.LoadLimits().Single(),date).Pending); }
            db.SetLimitEnabled("a",false); var paused=db.LoadLimits().Single(); Assert(!paused.Enabled && paused.Revision>rule.Revision);
            Assert(db.GetDay(date,TimeZoneInfo.Utc).Foreground==TimeSpan.FromMinutes(10));
            db.DeleteLimit("a"); Assert(db.LoadLimits().Count==0 && db.RecordedApps().Count==1);
        })),
        ("SQLite旧记录迁移不丢失、分析日期含未记录", () => WithDb((db,path)=>{
            Seed(db); using (var c=new SqliteConnection($"Data Source={path};Pooling=False")) {c.Open();using var cmd=c.CreateCommand();cmd.CommandText="DROP TABLE usage_limits; DROP TABLE limit_days; PRAGMA user_version=1";cmd.ExecuteNonQuery();}
            using var migrated=new UsageDatabase(path);
            var analysis=migrated.Analyze(new(2026,10,25),7,TimeZoneInfo.Utc);
            Assert(analysis.Days.Count==7 && analysis.RecordedDays==1 && analysis.Foreground.TotalMinutes==10);
            Assert(analysis.Summary().Contains("无记录日期不代表"));
        })),
        ("跨午夜分析和双口径总计一致",()=>WithDb((db,path)=>{
            var start=new DateTimeOffset(2026,10,24,23,58,0,TimeSpan.Zero);
            db.Save(new([new("s","s",new("a","A"),start,start.AddMinutes(5))],[new("i","s",start,start.AddMinutes(3))]));
            var analysis=db.Analyze(new(2026,10,25),7,TimeZoneInfo.Utc);
            Assert(analysis.Foreground.TotalMinutes==5 && analysis.WithoutIdle.TotalMinutes==2);
            Assert(analysis.Days[^2].Foreground.TotalMinutes==2 && analysis.Days[^1].Foreground.TotalMinutes==3);
            Assert(analysis.Apps.Single().Foreground==analysis.Foreground);
        })),
        ("AI汇总无路径与应用键，匿名后无原始名称",()=>{
            var report=new DayReport(new(2026,10,25),[new(new("C:\\secret\\editor.exe","MyPrivateEditor","C:\\secret\\editor.exe"),TimeSpan.FromMinutes(20),TimeSpan.FromMinutes(12))],[]);
            var analysis=new UsageAnalysis([report]); var payload=analysis.Payload(true);
            Assert(!payload.Contains("secret") && !payload.Contains("MyPrivateEditor") && payload.Contains("应用 1"));
            Assert(!analysis.Payload(false).Contains("secret"));
            using var json=JsonDocument.Parse(AiAnalysisClient.RequestBody("model",UsageAnalysis.DefaultPrompt,payload));
            Assert(json.RootElement.GetProperty("messages").GetArrayLength()==2);
        }),
        ("AI接口拒绝不安全地址、允许本机服务",()=>{
            foreach(var url in new[]{"http://example.com/v1/chat/completions","file:///tmp/a","https://user:secret@example.com","https://example.com/?key=secret"}) Throws<ArgumentException>(()=>AiAnalysisClient.ValidateEndpoint(url));
            Assert(AiAnalysisClient.ValidateEndpoint("http://localhost:11434/v1/chat/completions").IsLoopback);
        }),
        ("AI模拟响应成功、鉴权头不进入正文",()=>{
            using var client=new HttpClient(new FakeHandler((request,ct)=>{
                Assert(request.Headers.Authorization?.Parameter=="test-key");
                var body=request.Content!.ReadAsStringAsync(ct).GetAwaiter().GetResult(); Assert(!body.Contains("test-key"));
                return Task.FromResult(Response(HttpStatusCode.OK,"{\"choices\":[{\"message\":{\"content\":\"测试分析\"}}]}"));
            }));
            var output=AiAnalysisClient.SendAsync(client,"https://example.test/v1/chat/completions","test-key","{}",CancellationToken.None).GetAwaiter().GetResult(); Assert(output=="测试分析");
        }),
        ("AI服务错误不泄露响应、坏格式与超大响应可处理",()=>{
            using var client=new HttpClient(new FakeHandler((_,_)=>Task.FromResult(Response(HttpStatusCode.Unauthorized,"private-key-server-error"))));
            try {AiAnalysisClient.SendAsync(client,"https://example.test/api","key","{}",default).GetAwaiter().GetResult(); throw new Exception("应失败");} catch(InvalidOperationException e){Assert(!e.Message.Contains("private-key"));}
            foreach(var body in new[]{"not json","{\"choices\":[]}",new string('x',300000)}) {
                using var bad=new HttpClient(new FakeHandler((_,_)=>Task.FromResult(Response(HttpStatusCode.OK,body))));
                Throws<InvalidOperationException>(()=>AiAnalysisClient.SendAsync(bad,"https://example.test/api","","{}",default).GetAwaiter().GetResult());
            }
        }),
        ("AI请求支持取消",()=>{
            using var stop=new CancellationTokenSource(); stop.Cancel();
            using var client=new HttpClient(new FakeHandler(async (_,ct)=>{await Task.Delay(Timeout.Infinite,ct);return Response(HttpStatusCode.OK,"{}");}));
            Throws<OperationCanceledException>(()=>AiAnalysisClient.SendAsync(client,"https://example.test/api","","{}",stop.Token).GetAwaiter().GetResult());
        }),
        ("调度按严重优先，暂停会话不弹出，同日不重复",()=>WithDb((db,_)=>{
            var a=new DateTimeOffset(2026,10,25,9,0,0,TimeSpan.Zero);
            for(int i=0;i<3;i++) {var key="app"+i;db.Save(new([new(key,key,new(key,key),a.AddMinutes(i*10),a.AddMinutes(i*10+5))],[]));db.SaveLimit(new(key,1,UsageMetric.Foreground,(ReminderLevel)i));}
            var day=new DateOnly(2026,10,25);var report=db.GetDay(day,TimeZoneInfo.Utc);var rules=db.LoadLimits();
            LimitDayState State(UsageLimit rule)=>db.LoadLimitDay(rule,day);
            Assert(LimitDispatch.Next(rules,report,State,false) is null);
            for(int expected=2;expected>=0;expected--){var next=LimitDispatch.Next(rules,report,State,true)!;Assert((int)next.Rule.Level==expected);db.SaveLimitDay(LimitPolicy.Dismiss(next.State));}
            Assert(LimitDispatch.Next(rules,report,State,true) is null);
        }))
    ];
    private static HttpResponseMessage Response(HttpStatusCode status,string body)=>new(status){Content=new StringContent(body,Encoding.UTF8,"application/json")};
    private sealed class FakeHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> action):HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>action(request,cancellationToken); }
    private static void Assert(bool value) { if(!value) throw new Exception("断言失败"); }
    private static void Throws<T>(Action action) where T:Exception { try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name); }
    private static void Seed(UsageDatabase db) {var a=new DateTimeOffset(2026,10,25,9,0,0,TimeSpan.Zero);db.Save(new([new("x","x",new("a","应用A","C:\\apps\\a.exe"),a,a.AddMinutes(10))],[]));}
    private static void WithDb(Action<UsageDatabase,string> test) { var folder=Path.Combine(Path.GetTempPath(),"TraceTime-feature-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);try{var path=Path.Combine(folder,"usage.db");using var db=new UsageDatabase(path);test(db,path);}finally{Directory.Delete(folder,true);} }
}
