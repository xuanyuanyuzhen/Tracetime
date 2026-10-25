using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TraceTime.Core;

namespace TraceTime.App;

public partial class MainWindow
{
    private UsageAnalysis? _analysis;
    private AiSettings _aiSettings = new();
    private string _aiSettingsPath = "";
    private bool _loadingAi;
    private CancellationTokenSource? _aiCancellation;
    private PreparedAnalysis? _prepared;
    private string _lastAiOutput = "", _lastAiScope = "";
    private sealed record PreparedAnalysis(string Endpoint, string Model, string Body, string Scope, string Key);

    private void InitializeAnalysis()
    {
        _loadingAi = true;
        try
        {
            _aiSettingsPath = Path.Combine(Path.GetDirectoryName(_db.FilePath)!, "ai-settings.json"); _aiSettings = AiSettings.Load(_aiSettingsPath);
            AnalysisEnd.DisplayDateEnd = DateTime.Today; AnalysisEnd.SelectedDate = DateTime.Today;
            AiAnonymous.IsChecked = _aiSettings.Anonymous;
            UpdateAiServiceSummary();
        }
        finally { _loadingAi = false; }
    }
    private void ShowAnalysis(object sender, RoutedEventArgs e) { SetPage("analysis"); RefreshAnalysis(); }
    private void RefreshAnalysisClick(object sender, RoutedEventArgs e) => RefreshAnalysis();
    private void AnalysisChanged(object sender, RoutedEventArgs e) { if (_ready && !_loadingAi) RefreshAnalysis(); }
    private void AnalysisDateChanged(object sender, SelectionChangedEventArgs e) { if (_ready && !_loadingAi) RefreshAnalysis(); }
    private void RefreshAnalysis()
    {
        if (!_ready) return;
        try
        {
            InvalidateAiPreview(); _tracker?.Flush();
            var end = DateOnly.FromDateTime(AnalysisEnd.SelectedDate ?? DateTime.Today);
            AnalysisEnd.DisplayDateEnd = DateTime.Today;
            _analysis = _db.Analyze(end, Analysis30.IsChecked == true ? 30 : 7, TimeZoneInfo.Local);
            AnalysisTotals.Text = $"前台停留 {FormatTime(_analysis.Foreground)}   ·   扣除空闲后 {FormatTime(_analysis.WithoutIdle)}";
            TrendChart.Analysis = _analysis; LocalSummary.Text = _analysis.Summary();
            DailyValues.ItemsSource = new[] { new { Day = "日期", Foreground = "前台停留", WithoutIdle = "扣除空闲后" } }.Concat(_analysis.Days.Select(d => new { Day = d.Date.ToString("M月d日"), Foreground = d.Foreground == TimeSpan.Zero ? "未记录" : FormatTime(d.Foreground), WithoutIdle = d.Foreground == TimeSpan.Zero ? "未记录" : FormatTime(d.WithoutIdle) })).ToArray();
            RenderAnalysisApps(); ApplyAnalysisView();
        }
        catch (Exception ex) { ShowError("分析读取失败：" + ex.Message); }
    }
    private void AnalysisViewChanged(object sender, RoutedEventArgs e) { if (_ready) ApplyAnalysisView(); }
    private void ApplyAnalysisView()
    {
        TrendPanel.Visibility = TrendTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        AnalysisRankingPanel.Visibility = RankingTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        LocalSummary.Visibility = SummaryTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }
    private void AnalysisMetricChanged(object sender, RoutedEventArgs e) { if (_ready) RenderAnalysisApps(); }
    private void RenderAnalysisApps()
    {
        if (_analysis is null) return;
        var active = AnalysisIdle.IsChecked == true; var total = active ? _analysis.WithoutIdle : _analysis.Foreground;
        AnalysisApps.ItemsSource = _analysis.Apps.OrderByDescending(x => active ? x.WithoutIdle : x.Foreground).Select(x => new { Visual = _icons.Get(x.App), Name = x.App.Name, Duration = FormatTime(active ? x.WithoutIdle : x.Foreground), Percent = total.Ticks == 0 ? "0%" : ((active ? x.WithoutIdle : x.Foreground).Ticks / (double)total.Ticks).ToString("P1") }).ToArray();
        AnalysisNoApps.Visibility = total == TimeSpan.Zero ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OpenAiConfig(object sender, RoutedEventArgs e)
    {
        InvalidateAiPreview();
        var dialog=new AiConfigWindow(_aiSettings,_aiSettingsPath) { Owner=this };
        dialog.ShowDialog();
        if(dialog.Saved is { } settings) { _aiSettings=settings; UpdateAiServiceSummary(); AiFeedback.Text="服务配置已保存，尚未发送使用记录。"; }
    }
    private void UpdateAiServiceSummary()
    {
        AiServiceSummary.Text=string.IsNullOrWhiteSpace(_aiSettings.Model)?"尚未配置 AI 服务":(string.IsNullOrWhiteSpace(_aiSettings.Name)?"已配置 AI 服务":_aiSettings.Name)+" · "+_aiSettings.Model;
        AiServiceHint.Text=string.IsNullOrWhiteSpace(_aiSettings.Model)?"先配置服务，或直接使用本地分析。":_aiSettings.Endpoint;
    }
    private void AiAnonymityChanged(object sender, RoutedEventArgs e)
    {
        if(!_ready || _loadingAi) return;
        InvalidateAiPreview();
        try { var settings=_aiSettings with {Anonymous=AiAnonymous.IsChecked==true}; settings.Save(_aiSettingsPath); _aiSettings=settings; }
        catch { AiFeedback.Text="匿名选择本次有效，但未能保存，请检查文件夹权限。"; }
    }
    private void InvalidateAiPreview()
    {
        _aiCancellation?.Cancel(); _prepared = null; AiConsent.IsChecked = false; AiSend.IsEnabled = false; AiPreview.Text = ""; AiPreviewPanel.Visibility = Visibility.Collapsed;
    }
    private void PrepareAiPreview(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_analysis is null || _analysis.RecordedDays == 0) { AiFeedback.Text = "没有可分析的使用记录，请先记录一些应用时长。"; return; }
            var options = _aiSettings; AiAnalysisClient.ValidateEndpoint(options.Endpoint); var key = options.Key();
            if (key.Length == 0 && !new Uri(options.Endpoint).IsLoopback) { AiFeedback.Text = "请填写 API Key；本机无鉴权服务可留空。"; return; }
            var payload=_analysis.Payload(AiAnonymous.IsChecked==true);
            var body = AiAnalysisClient.RequestBody(options.Model, UsageAnalysis.DefaultPrompt, payload);
            _prepared = new(options.Endpoint, options.Model, body, $"{_analysis.Days.First().Date:yyyy-MM-dd} 至 {_analysis.Days.Last().Date:yyyy-MM-dd}", key);
            AiConsent.IsChecked = false; AiSend.IsEnabled = false; AiPreview.Text = payload;
            AiDestination.Text = $"发送到：{options.Endpoint}\n模型：{options.Model} · {_prepared.Scope}";
            AiPreviewPanel.Visibility = Visibility.Visible; AiFeedback.Text = "请检查预览并勾选同意，再发送。预览仅显示会外发的汇总数据。";
        }
        catch (ArgumentException ex) { AiFeedback.Text = ex.Message; }
        catch { AiFeedback.Text = "无法读取配置或密钥，请重新输入密钥后重试。"; }
    }
    private void AiConsentChanged(object sender, RoutedEventArgs e) { if (_ready) AiSend.IsEnabled = _prepared is not null && AiConsent.IsChecked == true && _aiCancellation is null; }
    private async void RunAiAnalysis(object sender, RoutedEventArgs e)
    {
        using var handler = new HttpClientHandler { AllowAutoRedirect = false };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        await SendPreparedAnalysisAsync(client);
    }
    private async Task SendPreparedAnalysisAsync(HttpClient client, TimeSpan? timeout = null)
    {
        if (_prepared is not { } request || AiConsent.IsChecked != true || _aiCancellation is not null) return;
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(90)); _aiCancellation = cancellation;
        AiSend.IsEnabled = false; AiCancel.IsEnabled = true; AiFeedback.Text = "正在请求分析…可取消。";
        try
        {
            var result = await AiAnalysisClient.SendAsync(client, request.Endpoint, request.Key, request.Body, cancellation.Token);
            if (cancellation.IsCancellationRequested) return;
            _lastAiOutput = result; _lastAiScope = request.Scope;
            AiResult.Text = $"分析范围：{request.Scope}\n模型：{request.Model}\n\n{result}";
            AiFeedback.Text = "分析完成。建议仅供自我回顾，不会自动修改额度。";
        }
        catch (OperationCanceledException) { AiFeedback.Text = "请求已取消或超过 90 秒，请稍后重试。"; }
        catch (InvalidOperationException ex) { AiFeedback.Text = ex.Message; }
        catch (HttpRequestException) { AiFeedback.Text = "连接失败，请检查网络、HTTPS 证书和接口地址。"; }
        catch { AiFeedback.Text = "分析失败，请检查服务配置后重试。"; }
        finally { _aiCancellation = null; _prepared = null; AiCancel.IsEnabled = false; AiConsent.IsChecked = false; AiSend.IsEnabled = false; }
    }
    private void CancelAiAnalysis(object sender, RoutedEventArgs e) => _aiCancellation?.Cancel();
    private void CopyAiData(object sender, RoutedEventArgs e)
    {
        if (_analysis is null) return;
        try { Clipboard.SetText(_analysis.Payload(AiAnonymous.IsChecked == true)); AiFeedback.Text = "汇总数据已复制。"; }
        catch { AiFeedback.Text = "剪贴板暂时不可用，请稍后再试。"; }
    }
    private void ExportAnalysis(object sender, RoutedEventArgs e)
    {
        if (_analysis is null) return;
        var dialog = new SaveFileDialog { Filter = "文本文件 (*.txt)|*.txt", FileName = "TraceTime-分析.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, _analysis.Summary() + (string.IsNullOrEmpty(_lastAiOutput) ? "" : "\n\nAI 回顾（" + _lastAiScope + "）\n" + _lastAiOutput), Encoding.UTF8); AiFeedback.Text = "分析已导出，未包含密钥。"; }
        catch { AiFeedback.Text = "导出失败，请选择可写的文件夹。"; }
    }
}
