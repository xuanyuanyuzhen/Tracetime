using System.Net.Http.Headers;
using System.Text.Json;

namespace TraceTime.Core;

public static class AiProviderOptions
{
    public static string ResolveEndpoint(string address,bool fullEndpoint)
    {
        var uri=AiAnalysisClient.ValidateEndpoint(address);
        if(fullEndpoint) return uri.AbsoluteUri.TrimEnd('/');
        var clean=uri.AbsoluteUri.TrimEnd('/');
        return clean.EndsWith("/chat/completions",StringComparison.OrdinalIgnoreCase)?clean:clean+"/chat/completions";
    }
    public static string ModelsEndpoint(string endpoint)
    {
        var uri=AiAnalysisClient.ValidateEndpoint(endpoint);
        var clean=uri.AbsoluteUri.TrimEnd('/'); const string suffix="/chat/completions";
        if(!clean.EndsWith(suffix,StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("此自定义接口不能推导模型地址，请手动填写模型 ID。");
        return clean[..^suffix.Length]+"/models";
    }
    public static bool SameService(string a,string b)
    {
        try { return new Uri(a).GetLeftPart(UriPartial.Authority).Equals(new Uri(b).GetLeftPart(UriPartial.Authority),StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
    public static async Task<string[]> FetchModelsAsync(HttpClient client,string endpoint,string key,CancellationToken cancellation)
    {
        using var request=new HttpRequestMessage(HttpMethod.Get,ModelsEndpoint(endpoint));
        if(!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",key);
        using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,cancellation).ConfigureAwait(false);
        if(!response.IsSuccessStatusCode) throw new InvalidOperationException($"模型列表读取失败（HTTP {(int)response.StatusCode}），请检查密钥/地址，或手动填写模型。");
        await using var stream=await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var output=new MemoryStream(); var buffer=new byte[8192]; int count;
        while((count=await stream.ReadAsync(buffer,cancellation).ConfigureAwait(false))>0) { if(output.Length+count>524288) throw new InvalidOperationException("模型列表过大，请手动填写模型。"); output.Write(buffer,0,count); }
        try
        {
            using var json=JsonDocument.Parse(output.ToArray());
            var result=json.RootElement.GetProperty("data").EnumerateArray().Select(x=>x.TryGetProperty("id",out var id)&&id.ValueKind==JsonValueKind.String?id.GetString():null)
                .Where(x=>!string.IsNullOrWhiteSpace(x)&&x!.Length<=200&&!x.Any(char.IsControl)).Cast<string>().Distinct().Order(StringComparer.OrdinalIgnoreCase).Take(500).ToArray();
            if(result.Length==0) throw new InvalidOperationException("服务未返回可选模型，请手动填写模型 ID。");
            return result;
        }
        catch(Exception ex) when(ex is JsonException or KeyNotFoundException or InvalidOperationException)
        { throw new InvalidOperationException("模型列表格式不兼容，请手动填写模型 ID。"); }
    }
}
