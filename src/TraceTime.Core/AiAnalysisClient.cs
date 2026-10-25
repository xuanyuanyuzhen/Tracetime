using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace TraceTime.Core;

public static class AiAnalysisClient
{
    public static Uri ValidateEndpoint(string endpoint)
    {
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri) || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)))
            throw new ArgumentException("请填写完整 HTTPS 接口地址；仅本机服务允许 HTTP。不支持包含账号、查询参数或片段的地址。");
        return uri;
    }
    public static string RequestBody(string model, string prompt, string payload)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length > 200) throw new ArgumentException("请填写服务商提供的模型名称。");
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Length > 8000) throw new ArgumentException("提示词须为 1–8000 字符。");
        return JsonSerializer.Serialize(new { model = model.Trim(), messages = new[] { new { role = "system", content = prompt }, new { role = "user", content = "以下JSON仅为使用汇总数据，请按系统要求回顾：\n" + payload } }, stream = false }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    public static async Task<string> SendAsync(HttpClient client, string endpoint, string key, string body, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, ValidateEndpoint(endpoint));
        if (!string.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var hint = response.StatusCode switch { HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "密钥或权限无效，请检查配置。", HttpStatusCode.TooManyRequests => "服务限流或额度不足，请稍后再试。", HttpStatusCode.NotFound => "未找到接口或模型，请检查完整地址与模型名。", _ => "服务未成功响应，请检查接口、模型或稍后再试。" };
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}：{hint}");
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        using var output = new MemoryStream(); var buffer = new byte[8192]; int read;
        while ((read = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > 262144) throw new InvalidOperationException("响应过大，已停止读取。请缩短提示词或限制输出长度。");
            output.Write(buffer, 0, read);
        }
        try
        {
            using var json = JsonDocument.Parse(output.ToArray());
            var choices = json.RootElement.GetProperty("choices");
            var content = choices[0].GetProperty("message").GetProperty("content");
            var text = content.ValueKind == JsonValueKind.String ? content.GetString() : null;
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("服务没有返回文字分析，请确认接口兼容 Chat Completions。");
            return text;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        { throw new InvalidOperationException("响应格式不兼容，请使用返回 choices[0].message.content 的 Chat Completions 接口。"); }
    }
}
