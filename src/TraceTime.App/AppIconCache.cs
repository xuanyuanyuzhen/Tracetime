using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TraceTime.Core;

namespace TraceTime.App;

internal sealed class AppIconCache
{
    private const int Capacity = 256;
    private readonly string _directory;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, AppVisual> _memory = new();
    private readonly SemaphoreSlim _worker = new(1,1);
    private readonly List<Task> _pending = [];
    private readonly Func<string?,ImageSource?> _extract;
    internal AppIconCache(string dataDirectory, Dispatcher dispatcher, Func<string?,ImageSource?>? extract = null)
    { _directory=Path.Combine(dataDirectory,"cache","icons"); _dispatcher=dispatcher; _extract=extract ?? Extract; }

    internal AppVisual Get(AppIdentity app)
    {
        if (_memory.TryGetValue(app.Key,out var cached)) return cached;
        if (_memory.Count>=Capacity) _memory.Remove(_memory.Keys.First());
        var visual=new AppVisual(app); _memory[app.Key]=visual;
        var task=LoadAsync(visual); _pending.RemoveAll(x=>x.IsCompleted); _pending.Add(task);
        return visual;
    }
    internal Task WhenReadyAsync() => Task.WhenAll(_pending.ToArray());
    internal string CacheFile(string key) => Path.Combine(_directory,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))+".png");
    private async Task LoadAsync(AppVisual visual)
    {
        var image=await Task.Run(async () => {
            await _worker.WaitAsync().ConfigureAwait(false);
            try
            {
                var path=CacheFile(visual.Key); ImageSource? stored=null;
                try
                {
                    if(File.Exists(path) && new FileInfo(path).Length<=131072)
                    {
                        var bitmap=new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption=BitmapCacheOption.OnLoad; bitmap.DecodePixelWidth=32;
                        using var stream=File.OpenRead(path); bitmap.StreamSource=stream; bitmap.EndInit(); bitmap.Freeze(); stored=bitmap;
                        if(File.GetLastWriteTimeUtc(path)>DateTime.UtcNow.AddDays(-30)) return stored;
                    }
                } catch { /* A corrupt cache is disposable; application identity is still shown. */ }
                var extracted=_extract(visual.App.ExecutablePath);
                if(extracted is BitmapSource source)
                {
                    try
                    {
                        Directory.CreateDirectory(_directory);
                        var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
                        using(var output=File.Create(path+".tmp")) encoder.Save(output);
                        File.Move(path+".tmp",path,true);
                        foreach(var old in new DirectoryInfo(_directory).EnumerateFiles("*.png").OrderByDescending(x=>x.LastWriteTimeUtc).Skip(Capacity)) old.Delete();
                    } catch { /* Cache failure must never stop time tracking. */ }
                }
                return extracted ?? stored;
            }
            catch { return null; }
            finally { _worker.Release(); }
        }).ConfigureAwait(false);
        if (!_dispatcher.HasShutdownStarted) await _dispatcher.InvokeAsync(()=>visual.SetIcon(image));
    }
    private static ImageSource? Extract(string? path)
    {
        // Only local executable resources; never fetch network icons or read desktop pixels.
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith(@"\\",StringComparison.Ordinal) || !File.Exists(path)) return null;
        using var icon=System.Drawing.Icon.ExtractAssociatedIcon(path);
        if(icon is null) return null;
        var bitmap=Imaging.CreateBitmapSourceFromHIcon(icon.Handle,Int32Rect.Empty,BitmapSizeOptions.FromWidthAndHeight(32,32)); bitmap.Freeze(); return bitmap;
    }
}

public sealed class AppVisual : INotifyPropertyChanged
{
    internal AppVisual(AppIdentity app) => App=app;
    public AppIdentity App { get; }
    public string Key => App.Key;
    public string Name => App.Name;
    public string Initial => string.IsNullOrWhiteSpace(Name)?"?":System.Globalization.StringInfo.GetNextTextElement(Name);
    public ImageSource? Icon { get; private set; }
    public Visibility LetterVisibility => Icon is null?Visibility.Visible:Visibility.Collapsed;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void SetIcon(ImageSource? image) { Icon=image; PropertyChanged?.Invoke(this,new(nameof(Icon))); PropertyChanged?.Invoke(this,new(nameof(LetterVisibility))); }
}

public sealed class AppIconView : Grid
{
    public static readonly DependencyProperty ValueProperty=DependencyProperty.Register(nameof(Value),typeof(AppVisual),typeof(AppIconView),new PropertyMetadata(null));
    public AppVisual? Value { get => (AppVisual?)GetValue(ValueProperty); set => SetValue(ValueProperty,value); }
    public AppIconView()
    {
        Width=28; Height=28; IsHitTestVisible=false;
        var border=new Border {CornerRadius=new CornerRadius(6)}; border.SetResourceReference(Border.BackgroundProperty,"AccentSoft"); Children.Add(border);
        var initial=new TextBlock {HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,FontWeight=FontWeights.SemiBold};
        initial.SetResourceReference(TextBlock.ForegroundProperty,"Ink"); initial.SetBinding(TextBlock.TextProperty,new Binding("Value.Initial") {Source=this}); initial.SetBinding(VisibilityProperty,new Binding("Value.LetterVisibility") {Source=this}); Children.Add(initial);
        var image=new Image {Width=24,Height=24}; image.SetBinding(Image.SourceProperty,new Binding("Value.Icon") {Source=this}); Children.Add(image);
    }
}
