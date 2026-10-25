using System.Net;
using System.Text;
using TraceTime.Core;
using TraceTime.Storage;

internal static class PolishTests
{
    internal static readonly (string Name,Action Test)[] All = [
        ("记录日期按本地日界跨午夜，结束边界不多算一天",()=>WithDb(db=>{
            var zone=TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
            var a=new DateTimeOffset(2026,10,24,23,59,0,TimeSpan.FromHours(8));
            db.Save(new([new("one","one",new("a","A"),a,a.AddMinutes(1))],[]));
            var dates=db.RecordedDates(new(2026,10,24),new(2026,10,26),zone); Check(dates.SetEquals([new(2026,10,24)]));
            db.Save(new([new("two","two",new("a","A"),a,a.AddMinutes(2))],[]));
            Check(db.RecordedDates(new(2026,10,24),new(2026,10,26),zone).SetEquals([new(2026,10,24),new(2026,10,25)]));
        })),
        ("空闲仍算有记录，零时长和暂停空档不标记",()=>WithDb(db=>{
            var a=new DateTimeOffset(2026,10,25,9,0,0,TimeSpan.Zero);
            db.Save(new([new("one","one",new("a","A"),a,a.AddMinutes(20))],[new("idle","one",a,a.AddMinutes(20))]));
            db.Save(new([new("zero","zero",new("a","A"),a.AddDays(1),a.AddDays(1))],[]));
            db.SaveGap(new("gap",a.AddDays(2),a.AddDays(2).AddMinutes(10),"已暂停"));
            Check(db.RecordedDates(new(2026,10,25),new(2026,10,27),TimeZoneInfo.Utc).SetEquals([new(2026,10,25)]));
            var revision=db.HistoryRevision; db.DeleteRange(a.Date,a.Date.AddDays(1));
            Check(db.HistoryRevision>revision && db.RecordedDates(new(2026,10,25),new(2026,10,27),TimeZoneInfo.Utc).Count==0);
        })),
        ("基础地址与完整接口补全且不重复路径",()=>{
            Check(AiProviderOptions.ResolveEndpoint("https://example.test/v1/",false)=="https://example.test/v1/chat/completions");
            Check(AiProviderOptions.ResolveEndpoint("https://example.test/v1/chat/completions",false)=="https://example.test/v1/chat/completions");
            Check(AiProviderOptions.ResolveEndpoint("https://example.test/custom/completions",true)=="https://example.test/custom/completions");
            Check(AiProviderOptions.ModelsEndpoint("http://localhost:11434/v1/chat/completions")=="http://localhost:11434/v1/models");
            Check(!AiProviderOptions.SameService("https://a.test/v1","https://b.test/v1"));
            Throws<ArgumentException>(()=>AiProviderOptions.ResolveEndpoint("http://remote.test/v1",false));
        }),
        ("获取模型只带鉴权，无使用记录，过滤无效重复ID",()=>{
            using var client=new HttpClient(new Handler((request,token)=>{
                Check(request.Method==HttpMethod.Get && request.RequestUri!.AbsolutePath=="/v1/models" && request.Content is null && request.Headers.Authorization!.Parameter=="fake-key");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"data\":[{\"id\":\"z-model\"},{\"id\":\"a-model\"},{\"id\":\"a-model\"},{\"id\":null}]}")});
            }));
            var models=AiProviderOptions.FetchModelsAsync(client,"https://example.test/v1/chat/completions","fake-key",default).GetAwaiter().GetResult();
            Check(models.SequenceEqual(new[]{"a-model","z-model"}));
        }),
        ("模型请求错误与取消不泄露服务原文",()=>{
            using var failed=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized){Content=new StringContent("secret-server-message")})));
            try {AiProviderOptions.FetchModelsAsync(failed,"https://example.test/v1/chat/completions","",default).GetAwaiter().GetResult();throw new Exception("应失败");}catch(InvalidOperationException e){Check(!e.Message.Contains("secret-server-message"));}
            using var invalid=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"data\":null}")})));
            Throws<InvalidOperationException>(()=>AiProviderOptions.FetchModelsAsync(invalid,"https://example.test/v1/chat/completions","",default).GetAwaiter().GetResult());
            using var cancel=new CancellationTokenSource();cancel.Cancel();
            Throws<OperationCanceledException>(()=>AiProviderOptions.FetchModelsAsync(invalid,"https://example.test/v1/chat/completions","",cancel.Token).GetAwaiter().GetResult());
        })
    ];
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)=>send(request,token);}
    private static void Check(bool condition) { if(!condition) throw new Exception("断言失败"); }
    private static void Throws<T>(Action run) where T:Exception {try{run();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static void WithDb(Action<UsageDatabase> action) {var directory=Path.Combine(Path.GetTempPath(),"TraceTime-calendar-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);try{using var db=new UsageDatabase(Path.Combine(directory,"usage.db"));action(db);}finally{Directory.Delete(directory,true);}}
}
