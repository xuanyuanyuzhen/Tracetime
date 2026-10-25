using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using TraceTime.App.Windows;
using TraceTime.Core;
using TraceTime.Storage;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace TraceTime.App;

public partial class MainWindow : Window
{
    private readonly UsageDatabase _db;
    private readonly TrackerController? _tracker;
    private readonly bool _demo;
    private readonly DispatcherTimer _refreshTimer;
    private readonly AppIconCache _icons;
    private readonly RecordedCalendar _recordedCalendar;
    private string? _lastIconApp;
    private DayReport? _report;
    private string? _selectedApp;
    private bool _ready, _refreshing;
    private DateTime _lastToday = DateTime.Today;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _trayPause;
    private Drawing.Icon? _trayIcon;
    private DateOnly SelectedDay => DateOnly.FromDateTime(DayPicker.SelectedDate ?? DateTime.Today);
    private bool WithoutIdle => ActiveMetric.IsChecked == true;

    internal MainWindow(UsageDatabase db, TrackerController? tracker, bool demo)
    {
        _db = db; _tracker = tracker; _demo = demo;
        _icons = new AppIconCache(Path.GetDirectoryName(db.FilePath)!,Dispatcher);
        _appearancePath = Path.Combine(Path.GetDirectoryName(db.FilePath)!, "appearance.json");
        _appearance = AppearanceSettings.Load(_appearancePath);
        ThemeManager.Apply(_appearance);
        InitializeComponent();
        _limits = _db.LoadLimits(); InitializeAnalysis();
        _recordedCalendar = new RecordedCalendar(db,DayPicker,AnalysisEnd,RangeStart,RangeEnd);
        SourceInitialized += (_, _) => ThemeManager.ApplyTitleBar(this);
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        DayPicker.DisplayDateEnd = DateTime.Today; DayPicker.SelectedDate = DateTime.Today;
        RangeStart.SelectedDate = DateTime.Today; RangeEnd.SelectedDate = DateTime.Today;
        RangeStart.DisplayDateEnd = RangeEnd.DisplayDateEnd = DateTime.Today;
        StoragePath.Text = db.FilePath;
        IdleInput.Text = db.LoadSettings().IdleMinutes.ToString(CultureInfo.InvariantCulture);
        try { StartupCheck.IsChecked = StartupRegistration.IsEnabled(); } catch { StartupCheck.IsChecked = false; }
        if (_demo) { Title = "TraceTime · 演示数据"; PauseButton.IsEnabled = false; StartupCheck.IsEnabled = false; }
        if (_tracker is not null) _tracker.Changed += UpdateStatus;
        _refreshTimer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => {
            if (IsVisible && OverviewPanel.Visibility == Visibility.Visible) RefreshReport();
            CheckLimits();
            _recordedCalendar.RefreshOpen();
            if (IsVisible && LimitsPanel.Visibility == Visibility.Visible) RenderLimitProgress();
        }, Dispatcher);
        _ready = true; SyncAppearanceControls(); SetPage("overview"); RefreshReport(); UpdateStatus();
    }

    private void RefreshReport()
    {
        if (!_ready) return;
        try
        {
            if(_lastToday!=DateTime.Today)
            {
                var followedToday=DayPicker.SelectedDate==_lastToday;
                _lastToday=DateTime.Today;
                DayPicker.DisplayDateEnd=RangeStart.DisplayDateEnd=RangeEnd.DisplayDateEnd=_lastToday;
                if(followedToday) { DayPicker.SelectedDate=_lastToday; return; }
            }
            _tracker?.Flush();
            _report = _db.GetDay(SelectedDay, TimeZoneInfo.Local);
            ForegroundTotal.Text = FormatTime(_report.Foreground);
            WithoutIdleTotal.Text = FormatTime(_report.WithoutIdle);
            DayLabel.Text = SelectedDay == DateOnly.FromDateTime(DateTime.Today) ? "今天 · " + DateTime.Today.ToString("M月d日 dddd", CultureInfo.GetCultureInfo("zh-CN")) : SelectedDay.ToString("yyyy年M月d日");
            NextDayButton.IsEnabled = SelectedDay < DateOnly.FromDateTime(DateTime.Today);
            RenderRows(); RenderTimeline(); UpdateStatus();
        }
        catch (Exception ex) { ShowError("读取记录失败：" + ex.Message); }
    }

    private void RenderRows()
    {
        if (_report is null) return;
        var selected = _selectedApp;
        var total = WithoutIdle ? _report.WithoutIdle : _report.Foreground;
        var sorted = _report.Apps.OrderByDescending(x => WithoutIdle ? x.WithoutIdle : x.Foreground).ToArray();
        var maximum = sorted.Length == 0 ? 0 : (WithoutIdle ? sorted[0].WithoutIdle : sorted[0].Foreground).TotalSeconds;
        var rows = sorted.Select((x, i) => new AppRow(x, WithoutIdle, total, maximum, i + 1,_icons)).ToArray();
        AppCountLabel.Text = $"{sorted.Length} 个应用";
        _refreshing = true;
        AppList.ItemsSource = rows;
        AppList.SelectedItem = rows.FirstOrDefault(x => x.Key == selected);
        _selectedApp = (AppList.SelectedItem as AppRow)?.Key;
        _refreshing = false;
        var empty=total<=TimeSpan.Zero;
        EmptyPanel.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        AppList.Visibility = RankingHeader.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        if(WithoutIdle && _report.Foreground>TimeSpan.Zero && empty)
        {
            EmptyTitle.Text="扣除空闲后，暂无时长";
            EmptyDescription.Text="已记录的前台时间均被判为空闲。切换到“前台停留”可查看完整记录。";
        }
        else if(SelectedDay==DateOnly.FromDateTime(DateTime.Today))
        {
            EmptyTitle.Text="今天的记录，从现在开始";
            EmptyDescription.Text=_tracker?.Paused==true?"记录已暂停，恢复后会继续累积。":"记录会自动累积，切换应用后再来看看。";
        }
        else
        {
            EmptyTitle.Text="这一天没有记录";
            EmptyDescription.Text="程序未运行的时段无法补记。可以选择其他日期，或回到今天查看。";
        }
    }

    private void RenderTimeline()
    {
        if (_report is null) return;
        Timeline.Report = _report; Timeline.SelectedAppKey = _selectedApp;
        SelectionLabel.Text = _selectedApp is null ? "全部应用" : _report.Apps.FirstOrDefault(x => x.App.Key == _selectedApp)?.App.Name ?? "全部应用";
        var entries = _report.Timeline.Where(x => _selectedApp is null || x.AppKey == _selectedApp).OrderByDescending(x => x.Start).ToArray();
        var apps=_report.Apps.ToDictionary(x=>x.App.Key,x=>x.App);
        DetailsList.ItemsSource = entries.Select(x => new {
            Visual = x.Kind=="gap" || string.IsNullOrEmpty(x.AppKey) ? null : _icons.Get(apps.GetValueOrDefault(x.AppKey) ?? new(x.AppKey,x.Label)),
            Range = $"{x.Start.LocalDateTime:HH:mm:ss} – {x.End.LocalDateTime:HH:mm:ss}",
            Label = x.Label + (x.Kind == "idle" ? " · 空闲" : ""),
            Duration = FormatTime(x.End - x.Start),
            AccessibleName=$"{x.Start.LocalDateTime:HH:mm:ss} 至 {x.End.LocalDateTime:HH:mm:ss}，{x.Label}，{(x.Kind=="idle"?"空闲，":"")}{FormatTime(x.End-x.Start)}"
        }).ToArray();
        DetailHint.Text = entries.Length == 0 ? "暂无时间片记录。" : $"全部 {entries.Length} 个时间片 · 列表可滚动，支持方向键及 Home / End 浏览";
    }

    private void UpdateStatus()
    {
        if (!_ready) return;
        if(_tracker is not null && _lastIconApp!=_tracker.CurrentApp.Key) { _lastIconApp=_tracker.CurrentApp.Key; _icons.Get(_tracker.CurrentApp); }
        if (_tracker is not null && !_tracker.CanPresentReminder) _limitAlert?.Hide();
        StatusText.Text = _demo ? "演示数据 · 不采集" : _tracker?.Status ?? "未记录";
        PauseButton.Content = _tracker?.Paused == true ? "恢复记录" : "暂停记录";
        StatusDot.Fill = ThemeManager.Get(_tracker?.Paused == true ? "Warning" : "Accent");
        if (_tray is not null) _tray.Text = "TraceTime · " + StatusText.Text;
        if (_trayPause is not null) _trayPause.Text = _tracker?.Paused == true ? "恢复记录" : "暂停记录";
        if (_tracker?.Error is { } error) ShowError(error);
        else if (ErrorBanner.Tag as string == "tracker") ErrorBanner.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message; ErrorBanner.Visibility = Visibility.Visible;
        ErrorBanner.Tag = _tracker?.Error == message ? "tracker" : "ui";
    }

    private void DateChanged(object sender, SelectionChangedEventArgs e) { if (_ready) { _selectedApp = null; RefreshReport(); } }
    private void MetricChanged(object sender, RoutedEventArgs e) { if (_ready) RenderRows(); }
    private void PreviousDay(object sender, RoutedEventArgs e) => DayPicker.SelectedDate = (DayPicker.SelectedDate ?? DateTime.Today).AddDays(-1);
    private void NextDay(object sender, RoutedEventArgs e) { if (SelectedDay < DateOnly.FromDateTime(DateTime.Today)) DayPicker.SelectedDate = (DayPicker.SelectedDate ?? DateTime.Today).AddDays(1); }
    private void Today(object sender, RoutedEventArgs e) => DayPicker.SelectedDate = DateTime.Today;
    private void TogglePause(object sender, RoutedEventArgs e) { _tracker?.TogglePause(); RefreshReport(); }
    private void AppSelected(object sender, SelectionChangedEventArgs e) { if (!_ready || _refreshing) return; _selectedApp = (AppList.SelectedItem as AppRow)?.Key; RenderTimeline(); }
    private void ClearSelection(object sender, RoutedEventArgs e) { _selectedApp = null; AppList.SelectedItem = null; RenderTimeline(); }
    private void TimelineExpanded(object sender, RoutedEventArgs e) { if (_ready) { TimelineExpander.Header = "收起每日时间线"; RenderTimeline(); } }
    private void TimelineCollapsed(object sender, RoutedEventArgs e) { if (_ready) TimelineExpander.Header = "展开每日时间线"; }

    private void ShowOverview(object sender, RoutedEventArgs e)
    {
        SetPage("overview"); RefreshReport();
    }
    private void ShowSettings(object sender, RoutedEventArgs e)
    {
        SetPage("settings");
        IdleInput.Text = _db.LoadSettings().IdleMinutes.ToString(CultureInfo.InvariantCulture);
        RangeStart.SelectedDate = RangeEnd.SelectedDate = SelectedDay.ToDateTime(TimeOnly.MinValue);
    }

    private void SavePreferences(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(IdleInput.Text, out var minutes) || minutes is < 1 or > 60) { SettingsFeedback.Text = "请输入 1–60 之间的整数分钟数。"; return; }
        var oldStartup = false; var startupChanged = false;
        try
        {
            if (!_demo)
            {
                oldStartup = StartupRegistration.IsEnabled();
                if (oldStartup != (StartupCheck.IsChecked == true)) { StartupRegistration.SetEnabled(StartupCheck.IsChecked == true); startupChanged = true; }
            }
            if (_tracker is not null) _tracker.UpdateSettings(new(minutes)); else _db.SaveSettings(new(minutes));
            SettingsFeedback.Text = "已保存。新的空闲阈值用于此后的记录。";
        }
        catch (Exception ex)
        {
            if (startupChanged) { try { StartupRegistration.SetEnabled(oldStartup); } catch { } }
            SettingsFeedback.Text = "保存失败：" + ex.Message;
        }
    }

    private (DateOnly First, DateOnly Last) SelectedRange()
    {
        if (RangeStart.SelectedDate is not { } first || RangeEnd.SelectedDate is not { } last || last < first || (last-first).TotalDays > 3660)
            throw new InvalidOperationException("请选择有效的起止日期（范围不超过十年）。");
        return (DateOnly.FromDateTime(first), DateOnly.FromDateTime(last));
    }
    private void Export(object sender, RoutedEventArgs e)
    {
        try
        {
            var (first,last) = SelectedRange();
            var dialog = new SaveFileDialog { Filter = "CSV 文件 (*.csv)|*.csv", FileName = $"TraceTime_{first:yyyyMMdd}_{last:yyyyMMdd}.csv", AddExtension = true };
            if (dialog.ShowDialog(this) != true) return;
            _tracker?.Flush();
            if (_tracker?.Error is { } error) throw new InvalidOperationException(error);
            _db.ExportCsv(dialog.FileName,first,last,TimeZoneInfo.Local); SettingsFeedback.Text = "汇总已导出：" + dialog.FileName;
        }
        catch(Exception ex) { MessageBox.Show(this,ex.Message,"导出失败",MessageBoxButton.OK,MessageBoxImage.Warning); }
    }
    private void DeleteRecords(object sender, RoutedEventArgs e)
    {
        try
        {
            var (first,last)=SelectedRange();
            if(MessageBox.Show(this,$"删除 {first:yyyy-MM-dd} 至 {last:yyyy-MM-dd} 的使用记录？\n此操作无法撤销，其他日期和设置会保留。", "删除记录",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes) return;
            var (start,_)=ReportBuilder.Bounds(first,TimeZoneInfo.Local); var (_,end)=ReportBuilder.Bounds(last,TimeZoneInfo.Local);
            if(_tracker is not null) _tracker.DeleteRange(start,end); else _db.DeleteRange(start,end);
            SettingsFeedback.Text="所选记录已删除。正在记录时，新的使用时长会继续累积。"; RefreshReport();
        }
        catch(Exception ex) { MessageBox.Show(this,ex.Message,"删除失败",MessageBoxButton.OK,MessageBoxImage.Warning); }
    }
    private void OpenDataFolder(object sender, RoutedEventArgs e)
    { try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Path.GetDirectoryName(_db.FilePath)}\"") { UseShellExecute=true }); } catch(Exception ex) { ShowError(ex.Message); } }
    private void ExitApp(object sender, RoutedEventArgs e) => ((App)Application.Current).ExitApp();
    private void WindowClosing(object? sender, CancelEventArgs e) { if(!((App)Application.Current).Exiting) { e.Cancel=true; Hide(); } }
    internal void Reveal() { Show(); if(WindowState==WindowState.Minimized) WindowState=WindowState.Normal; Activate(); RefreshReport(); }

    internal void VerifyViewFlow()
    {
        DayPicker.SelectedDate=DateTime.Today.AddDays(-1);
        if(SelectedDay!=DateOnly.FromDateTime(DateTime.Today.AddDays(-1))) throw new Exception("历史日期切换失败");
        if(_report!.Foreground==TimeSpan.Zero && !EmptyDescription.Text.Contains("无法补记")) throw new Exception("历史空状态提示不正确");
        Today(this,new RoutedEventArgs()); ActiveMetric.IsChecked=true;
        if(!WithoutIdle) throw new Exception("口径切换失败");
        TimelineExpander.IsExpanded=true; RenderTimeline();
        ShowSettings(this,new RoutedEventArgs());
        if(SettingsPanel.Visibility!=Visibility.Visible) throw new Exception("设置页面未显示");
        ShowOverview(this,new RoutedEventArgs());
        if(OverviewPanel.Visibility!=Visibility.Visible) throw new Exception("概览页面未显示");
        var a=DateTimeOffset.UtcNow.AddHours(-2); var app=new AppIdentity("qa","验证应用");
        _report=new(SelectedDay,[new(app,TimeSpan.FromMinutes(90),TimeSpan.Zero)],
            Enumerable.Range(0,45).Select(i=>new TimelineEntry(a.AddMinutes(i*2),a.AddMinutes(i*2+2),app.Key,app.Name,"idle")).ToArray());
        _selectedApp=null; ActiveMetric.IsChecked=true; RenderRows(); RenderTimeline();
        if(EmptyTitle.Text!="扣除空闲后，暂无时长" || AppList.Visibility!=Visibility.Collapsed) throw new Exception("全空闲口径状态错误");
        if(DetailsList.Items.Count!=45) throw new Exception("时间片列表截断");
        RefreshReport();
    }

    internal void InstallTray()
    {
        using var bitmap=new Drawing.Bitmap(32,32);
        using(var graphics=Drawing.Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode=Drawing.Drawing2D.SmoothingMode.AntiAlias;
            graphics.Clear(Drawing.Color.Transparent);
            using var pen=new Drawing.Pen(Drawing.Color.FromArgb(36,119,123),3);
            graphics.DrawEllipse(pen,3,3,26,26); graphics.DrawLines(pen,[new(16,8),new(16,16),new(23,20)]);
        }
        var iconHandle=bitmap.GetHicon();
        using(var original=Drawing.Icon.FromHandle(iconHandle)) _trayIcon=(Drawing.Icon)original.Clone();
        Icon=Imaging.CreateBitmapSourceFromHIcon(iconHandle,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());
        DestroyIcon(iconHandle);
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("打开 TraceTime",null,(_,_)=>Reveal());
        menu.Items.Add("设置",null,(_,_)=> { Reveal(); ShowSettings(this,new RoutedEventArgs()); });
        _trayPause=new Forms.ToolStripMenuItem("暂停记录",null,(_,_)=> { _tracker?.TogglePause(); RefreshReport(); }) {Enabled=!_demo};
        menu.Items.Add(_trayPause); menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出",null,(_,_)=>((App)Application.Current).ExitApp());
        _tray=new Forms.NotifyIcon {Icon=_trayIcon,Text="TraceTime",Visible=true,ContextMenuStrip=menu};
        _tray.MouseClick+=(_,e)=> { if(e.Button==Forms.MouseButtons.Left) Reveal(); };
        UpdateStatus();
    }
    internal void DisposeTray()
    {
        _aiCancellation?.Cancel(); CloseLimitAlert();
        SystemEvents.UserPreferenceChanged -= SystemThemeChanged;
        _refreshTimer.Stop(); if(_tracker is not null) _tracker.Changed-=UpdateStatus;
        if(_tray is not null) { _tray.Visible=false; _tray.ContextMenuStrip?.Dispose(); _tray.Dispose(); }
        _trayIcon?.Dispose();
    }

    internal void CloseTransientViews() { DayPicker.IsDropDownOpen = false; LimitAppPicker.IsDropDownOpen = false; CloseLimitAlert(); }

    internal void Capture(string path)
    {
        if(!_demo) throw new InvalidOperationException("只有隔离演示模式可以生成界面检查图片。");
        VerifyAppearanceFlow();
        VerifyRecordedCalendar(DayPicker,DateOnly.FromDateTime(DateTime.Today),DateOnly.FromDateTime(DateTime.Today).AddDays(-1));
        ChangeAppearance(new("light","glass"), false);
        Width=1160; Height=860; TimelineExpander.IsExpanded=false; Save(path);
        ChangeAppearance(new("dark","glass"), false); Save(Path.ChangeExtension(path,"dark.png"));
        ShowAppearance(this,new()); Save(Path.ChangeExtension(path,"appearance-dark.png"));
        ChangeAppearance(new("light","glass"),false); Save(Path.ChangeExtension(path,"appearance.png"));
        ShowOverview(this,new());
        foreach(var material in new[]{"soft","minimal"})
        foreach(var mode in new[]{"light","dark"})
        {
            ChangeAppearance(new(mode,material,"#3564C0"),false); Save(Path.ChangeExtension(path,material+"-"+mode+".png"));
        }
        ChangeAppearance(new("dark","glass"),false);
        DayPicker.IsDropDownOpen=true; UpdateLayout();
        Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
        if(DayPicker.Template.FindName("PART_Popup",DayPicker) is System.Windows.Controls.Primitives.Popup {Child:FrameworkElement calendar})
        {
            var item=VisualChildren(calendar).OfType<System.Windows.Controls.Primitives.CalendarItem>().Single();
            var month=(Grid)item.Template.FindName("PART_MonthView",item);
            var headings=month.Children.OfType<TextBlock>().Where(x=>Grid.GetRow(x)==0).ToArray();
            if(headings.Length!=7 || headings.Any(x=>string.IsNullOrWhiteSpace(x.Text) || x.ActualHeight<1))
                throw new Exception("日历未呈现七个星期标题");
            SaveElement(calendar,Path.ChangeExtension(path,"calendar-dark.png"));
            var pickerCalendar=VisualChildren(calendar).OfType<System.Windows.Controls.Calendar>().FirstOrDefault() ?? calendar as System.Windows.Controls.Calendar ?? throw new Exception("日历控件缺失");
            pickerCalendar.DisplayMode=CalendarMode.Year; calendar.UpdateLayout();
            Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            var year=(Grid)item.Template.FindName("PART_YearView",item);
            if(year.Visibility!=Visibility.Visible || year.Children.Count!=12) throw new Exception("月份选择不可用");
            SaveElement(calendar,Path.ChangeExtension(path,"calendar-year-dark.png"));
            pickerCalendar.DisplayMode=CalendarMode.Decade; calendar.UpdateLayout();
            if(year.Visibility!=Visibility.Visible) throw new Exception("年份选择不可用");
            pickerCalendar.DisplayMode=CalendarMode.Month;
        }
        else throw new Exception("日历弹出模板未连接");
        DayPicker.IsDropDownOpen=false;
        ChangeAppearance(new("light","glass"),false); TimelineExpander.IsExpanded=true;
        RenderTimeline(); OverviewPanel.ScrollToBottom(); Save(Path.ChangeExtension(path,"timeline-icons.png"));
        foreach(var row in DetailsList.Items)
        {
            var hasIcon=row.GetType().GetProperty("Visual")?.GetValue(row) is AppVisual;
            var name=(string?)row.GetType().GetProperty("Label")?.GetValue(row);
            if(name is not null && _report!.Apps.Any(x=>name.StartsWith(x.App.Name,StringComparison.Ordinal)) && !hasIcon) throw new Exception("应用时间片图标缺失");
        }
        OverviewPanel.ScrollToTop();
        ShowSettings(this,new RoutedEventArgs()); Save(Path.ChangeExtension(path,"settings.png"));
        var primary=VisualChildren(SettingsPanel).OfType<Button>().Single(x=>Equals(x.Content,"保存偏好"));
        var primaryText=VisualChildren(primary).OfType<TextBlock>().Single(x=>x.Text=="保存偏好");
        if(primaryText.Foreground is not SolidColorBrush foreground || primary.Background is not SolidColorBrush background || ThemeManager.Contrast(foreground.Color,background.Color)<4.5)
            throw new Exception("主要按钮的实际文字对比度不足");
        ChangeAppearance(new("dark","glass"),false); Save(Path.ChangeExtension(path,"settings-dark.png"));
        ChangeAppearance(new("light","glass"),false);
        ShowOverview(this,new RoutedEventArgs()); Width=900; Height=680; Save(Path.ChangeExtension(path,"compact.png"));
        ShowAppearance(this,new()); Save(Path.ChangeExtension(path,"appearance-compact.png"));
        ShowOverview(this,new()); Width=1160; Height=860; DayPicker.SelectedDate=DateTime.Today.AddDays(-1); Save(Path.ChangeExtension(path,"empty.png"));
        DayPicker.SelectedDate=DateTime.Today.AddDays(-2); ActiveMetric.IsChecked=true; Save(Path.ChangeExtension(path,"all-idle.png"));
        ForegroundMetric.IsChecked=true; OverviewPanel.ScrollToBottom(); Save(Path.ChangeExtension(path,"details.png"));
        VerifyFeatureViews(path,Save,SaveElement);
        File.WriteAllText(Path.ChangeExtension(path,"checks.txt"),"PASS appearance controls, persistence, invalid colors, reset, 54 surface and actual selected-control contrast combinations, unchanged usage data, seven weekday headings, year picker, rendered primary button contrast, and theme captures");
        void Save(string target)
        {
            UpdateLayout();
            Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);
            SaveElement((FrameworkElement)Content,target);
        }
        void SaveElement(FrameworkElement content,string target)
        {
            content.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);
            bitmap.Render(content); var encoder=new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!); using var stream=File.Create(target); encoder.Save(stream);
        }
    }
    private static IEnumerable<DependencyObject> VisualChildren(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i); yield return child;
            foreach(var descendant in VisualChildren(child)) yield return descendant;
        }
    }
    internal static string FormatTime(TimeSpan value)
    {
        if(value.TotalHours>=1) return $"{(int)value.TotalHours} 小时 {value.Minutes} 分";
        if(value.TotalMinutes>=1) return $"{(int)value.TotalMinutes} 分 {value.Seconds} 秒";
        return $"{Math.Max(0,(int)value.TotalSeconds)} 秒";
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}

internal sealed class AppRow
{
    public string Key {get;}
    public string Name {get;}
    public string Initial => string.IsNullOrWhiteSpace(Name)?"?":Name[..1];
    public string Duration {get;}
    public string Percent {get;}
    public double BarWidth {get;}
    public int Rank {get;}
    public GridLength BarFraction {get;}
    public GridLength RemainingFraction {get;}
    public Brush Brush {get;}
    public Brush Tint {get;}
    public AppVisual Visual {get;}
    public AppRow(AppUsage row,bool withoutIdle,TimeSpan total,double maximum,int rank,AppIconCache icons)
    {
        Key=row.App.Key; Name=row.App.Name; var duration=withoutIdle?row.WithoutIdle:row.Foreground;
        Duration=MainWindow.FormatTime(duration); Percent=total.Ticks==0?"0%":(100d*duration.Ticks/total.Ticks).ToString("0.0",CultureInfo.CurrentCulture)+"%";
        BarWidth=maximum<=0?0:Math.Clamp(duration.TotalSeconds/maximum*200,0,200);
        Rank=rank;
        var fraction=maximum<=0?0:Math.Clamp(duration.TotalSeconds/maximum,0,1);
        BarFraction=new GridLength(fraction,GridUnitType.Star); RemainingFraction=new GridLength(1-fraction,GridUnitType.Star);
        Brush=Palette.ForApp(Key); Tint=ThemeManager.Get("AccentSoft"); Visual=icons.Get(row.App);
    }
}

internal static class Palette
{
    private static readonly string[] Colors=["#24777B","#576CA8","#9B692F","#7F65A0","#3F7F62","#A85D61","#386F98"];
    private static readonly string[] DarkColors=["#70C5C8","#93A7E8","#D6AB71","#BB9ADB","#84BF9A","#DE99AA","#7ABAE4"];
    public static SolidColorBrush Brush(string hex) { var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brush; }
    public static SolidColorBrush ForApp(string key) { uint hash=2166136261; foreach(var c in key) hash=(hash^c)*16777619; return Brush((ThemeManager.IsDark?DarkColors:Colors)[hash%Colors.Length]); }
}
