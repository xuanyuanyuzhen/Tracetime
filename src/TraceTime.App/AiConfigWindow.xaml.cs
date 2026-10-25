using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using TraceTime.Core;

namespace TraceTime.App;

public partial class AiConfigWindow : Window
{
    private readonly AiSettings _original;
    private readonly string _path;
    private bool _ready;
    private bool _clearKey;
    private string _provider="compatible";
    private string[] _models=[];
    private CancellationTokenSource? _fetching;
    internal AiSettings? Saved { get; private set; }
    internal AiConfigWindow(AiSettings settings,string path)
    {
        _original=settings; _path=path; InitializeComponent();
        MaxHeight=SystemParameters.WorkArea.Height;
        var endpoint=settings.Endpoint;
        if(Uri.TryCreate(endpoint,UriKind.Absolute,out var uri)) _provider=uri.IsLoopback?"local":uri.Host=="api.openai.com"?"openai":"compatible";
        OpenAiPreset.IsChecked=_provider=="openai"; LocalPreset.IsChecked=_provider=="local"; CustomPreset.IsChecked=_provider=="compatible";
        var standard=endpoint.TrimEnd('/').EndsWith("/chat/completions",StringComparison.OrdinalIgnoreCase);
        BaseAddress.IsChecked=standard; FullAddress.IsChecked=!standard;
        AddressInput.Text=standard?endpoint.TrimEnd('/')[..^"/chat/completions".Length]:endpoint;
        ConfigName.Text=settings.Name; ManualModel.Text=settings.Model;
        _models=(settings.Models ?? []).Where(x=>!string.IsNullOrWhiteSpace(x)).Take(500).ToArray(); ModelList.ItemsSource=_models;
        ModelList.SelectedItem=_models.FirstOrDefault(x=>x==settings.Model);
        SelectModelMode.IsChecked=ModelList.SelectedItem is not null; ManualModelMode.IsChecked=ModelList.SelectedItem is null;
        _ready=true; UpdateHints(); UpdateModelMode();
        SourceInitialized+=(_,_)=>ThemeManager.ApplyTitleBar(this);
        Closing+=(_,_)=>_fetching?.Cancel();
        Closed+=(_,_)=>KeyInput.Clear();
    }
    private void ProviderChanged(object sender,RoutedEventArgs e)
    {
        if(!_ready || sender is not RadioButton {Tag:string provider}) return;
        _provider=provider; _fetching?.Cancel();
        BaseAddress.IsChecked=true;
        AddressInput.Text=provider switch {"openai"=>"https://api.openai.com/v1","local"=>"http://localhost:11434/v1",_=>""};
        KeyInput.Clear(); _models=[]; ModelList.ItemsSource=_models; ManualModel.Text=""; ManualModelMode.IsChecked=true; UpdateHints();
    }
    private void AddressModeChanged(object sender,RoutedEventArgs e)
    {
        if(!_ready) return;
        var value=AddressInput.Text.Trim().TrimEnd('/');
        if(FullAddress.IsChecked==true && value.Length>0 && !value.EndsWith("/chat/completions",StringComparison.OrdinalIgnoreCase)) AddressInput.Text=value+"/chat/completions";
        if(BaseAddress.IsChecked==true && value.EndsWith("/chat/completions",StringComparison.OrdinalIgnoreCase)) AddressInput.Text=value[..^"/chat/completions".Length];
        ConnectionChanged(sender,null!);
    }
    private void ConnectionChanged(object sender,TextChangedEventArgs e) { if(_ready) { _fetching?.Cancel(); UpdateHints(); } }
    private void KeyChanged(object sender,RoutedEventArgs e) { if(_ready) { _fetching?.Cancel(); UpdateHints(); } }
    private string Endpoint() => AiProviderOptions.ResolveEndpoint(AddressInput.Text,FullAddress.IsChecked==true);
    private bool CanReuseKey() => !_clearKey && AiProviderOptions.SameService(_original.Endpoint,AddressInput.Text.Trim()) && !string.IsNullOrEmpty(_original.ProtectedKey);
    private string DraftKey() => KeyInput.Password.Length>0?KeyInput.Password:CanReuseKey()?_original.Key():"";
    private void UpdateHints()
    {
        ProviderHint.Text=_provider switch {"openai"=>"使用 OpenAI 官方接口；选择你账号可用的文字模型。","local"=>"连接本机兼容服务，默认地址可修改；无需鉴权时密钥留空。",_=>"连接提供 Chat Completions 格式的服务，地址与模型由服务商提供。"};
        try { EndpointHint.Text="实际分析接口："+Endpoint(); } catch { EndpointHint.Text=FullAddress.IsChecked==true?"例如 https://你的服务/v1/chat/completions":"例如 https://你的服务/v1，自动补全 /chat/completions"; }
        KeyHint.Text=_clearKey?"保存后清除原密钥；取消可保留原配置。":CanReuseKey()?"已保存加密密钥，留空继续使用。":"请填写此服务的密钥；本机无鉴权服务可留空。";
        ClearKeyButton.IsEnabled=!string.IsNullOrEmpty(_original.ProtectedKey)&&!_clearKey;
    }
    private void ClearStoredKey(object sender,RoutedEventArgs e) { _clearKey=true; KeyInput.Clear(); _fetching?.Cancel(); UpdateHints(); }
    private void ModelModeChanged(object sender,RoutedEventArgs e) { if(_ready) UpdateModelMode(); }
    private void UpdateModelMode()
    {
        var select=SelectModelMode.IsChecked==true;
        ModelList.Visibility=select?Visibility.Visible:Visibility.Collapsed; ManualModel.Visibility=select?Visibility.Collapsed:Visibility.Visible;
        if(!select && ModelList.SelectedItem is string id) ManualModel.Text=id;
    }
    private async void FetchModels(object sender,RoutedEventArgs e)
    {
        using var handler=new HttpClientHandler {AllowAutoRedirect=false}; using var client=new HttpClient(handler) {Timeout=Timeout.InfiniteTimeSpan};
        await FetchModelsAsync(client);
    }
    internal async Task FetchModelsAsync(HttpClient client)
    {
        if(_fetching is not null) { _fetching.Cancel(); return; }
        using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var endpoint=Endpoint(); var key=DraftKey();
            if(key.Length==0 && !new Uri(endpoint).IsLoopback) { Feedback.Text="请先填写此服务的 API Key。"; return; }
            _fetching=cancel; FetchModelsButton.Content="取消获取"; SaveButton.IsEnabled=false; Feedback.Text="正在获取模型列表…";
            var models=await AiProviderOptions.FetchModelsAsync(client,endpoint,key,cancel.Token);
            if(cancel.IsCancellationRequested || !IsVisible) return;
            _models=models; ModelList.ItemsSource=models; ModelList.SelectedItem=models.FirstOrDefault(x=>x==ManualModel.Text) ?? models.First(); SelectModelMode.IsChecked=true;
            Feedback.Text=$"已获取 {models.Length} 个模型，请选择支持文字分析的模型。";
        }
        catch(OperationCanceledException) { Feedback.Text="获取已取消或超时，可手动填写模型。"; }
        catch(ArgumentException ex) { Feedback.Text=ex.Message; }
        catch(InvalidOperationException ex) { Feedback.Text=ex.Message; }
        catch { Feedback.Text="连接或密钥读取失败，请检查配置；也可手动填写模型。"; }
        finally { _fetching=null; FetchModelsButton.Content="获取模型列表"; SaveButton.IsEnabled=true; }
    }
    private void SaveConfig(object sender,RoutedEventArgs e)
    {
        try
        {
            var endpoint=Endpoint(); var model=SelectModelMode.IsChecked==true?ModelList.SelectedItem as string:ManualModel.Text.Trim();
            AiAnalysisClient.RequestBody(model ?? "",UsageAnalysis.DefaultPrompt,"{}");
            var protectedKey=CanReuseKey()?_original.ProtectedKey:"";
            if(KeyInput.Password.Length>0)
            {
                var bytes=Encoding.UTF8.GetBytes(KeyInput.Password);
                try { protectedKey=Convert.ToBase64String(UserSecret.Protect(bytes)); } finally { CryptographicOperations.ZeroMemory(bytes); }
            }
            var result=new AiSettings(endpoint,model!,protectedKey,_original.Anonymous,_provider,ConfigName.Text.Trim(),_models.Take(500).ToArray());
            result.Save(_path); Saved=result; Close();
        }
        catch(ArgumentException ex) { Feedback.Text=ex.Message; }
        catch { Feedback.Text="保存失败，请检查数据文件夹权限或重新输入密钥。"; }
    }
    private void CancelConfig(object sender,RoutedEventArgs e) => Close();
}
