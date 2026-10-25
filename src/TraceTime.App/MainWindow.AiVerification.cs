using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace TraceTime.App;

public partial class MainWindow
{
    internal async Task VerifyAiUiAsync(string path)
    {
        if (!_demo) throw new InvalidOperationException("模拟接口验证仅允许演示模式。");
        void Check(bool condition,string reason) { if(!condition) throw new Exception(reason); }
        void Prepare() { PrepareAiPreview(this,new()); Check(_prepared is not null,"预览未就绪"); AiConsent.IsChecked=true; }
        ShowAnalysis(this,new()); AiEndpoint.Text="http://localhost:11434/v1/chat/completions"; AiModel.Text="mock-only";
        using(var ok=new HttpClient(new UiMockHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) {Content=new StringContent("{\"choices\":[{\"message\":{\"content\":\"模拟分析结果\"}}]}")}))))
        { Prepare(); await SendPreparedAnalysisAsync(ok); Check(AiResult.Text.Contains("模拟分析结果") && !AiSend.IsEnabled && !AiCancel.IsEnabled,"成功响应未更新界面或未重置状态"); }
        using(var denied=new HttpClient(new UiMockHandler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) {Content=new StringContent("do-not-show-server-secrets")}))))
        { Prepare(); await SendPreparedAnalysisAsync(denied); Check(AiFeedback.Text.Contains("401") && !AiFeedback.Text.Contains("server-secrets"),"错误提示不正确"); }
        using(var delayed=new HttpClient(new UiMockHandler(async (_,ct)=>{await Task.Delay(Timeout.Infinite,ct);return new HttpResponseMessage(HttpStatusCode.OK);})))
        {
            Prepare(); var pending=SendPreparedAnalysisAsync(delayed); Check(AiCancel.IsEnabled,"请求中不能取消");
            AiModel.Text="new-range-model"; await pending;
            Check(AiFeedback.Text.Contains("取消") && _prepared is null && !AiSend.IsEnabled,"变更配置未取消旧请求");
            Prepare(); await SendPreparedAnalysisAsync(delayed,TimeSpan.FromMilliseconds(20)); Check(AiFeedback.Text.Contains("取消") && !AiCancel.IsEnabled,"超时未恢复界面");
        }
        _lastAiOutput="";_lastAiScope="";
        File.WriteAllText(Path.ChangeExtension(path,"ai-flow.txt"),"PASS mock AI UI success, HTTP error redaction, config-change cancellation, timeout and consent reset. No network request was sent.");
    }
    private sealed class UiMockHandler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage message,CancellationToken token)=>send(message,token); }
}
