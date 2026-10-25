using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TraceTime.Core;

namespace TraceTime.App;

public sealed class TimelineView : FrameworkElement
{
    private DayReport? _report;
    private string? _selected;
    public DayReport? Report {get=>_report; set{_report=value; InvalidateVisual();}}
    public string? SelectedAppKey {get=>_selected; set{_selected=value; InvalidateVisual();}}
    public TimelineView() { ToolTip="时间线"; Focusable=true; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width=Math.Max(1,ActualWidth-4); var x=2d;
        dc.DrawRoundedRectangle(ThemeManager.Get("Track"),null,new(x,32,width,32),4,4);
        if(_report is not null)
        {
            var (a,b)=ReportBuilder.Bounds(_report.Date,TimeZoneInfo.Local);
            var span=(b-a).TotalSeconds;
            foreach(var entry in _report.Timeline)
            {
                var left=x+Math.Clamp((entry.Start-a).TotalSeconds/span,0,1)*width;
                var right=x+Math.Clamp((entry.End-a).TotalSeconds/span,0,1)*width;
                if(right<=left) continue;
                var brush=entry.Kind=="gap"?ThemeManager.Get("Gap"):entry.Kind=="idle"?ThemeManager.Get("Idle"):Palette.ForApp(entry.AppKey);
                dc.PushOpacity(_selected is null || entry.AppKey==_selected?1:0.2);
                dc.DrawRectangle(brush,null,new(left,32,Math.Max(1,right-left),32));
                if(entry.Kind=="idle") dc.DrawRectangle(Palette.ForApp(entry.AppKey),null,new(left,32,Math.Max(1,right-left),2));
                dc.Pop();
            }
        }
        var bounds = _report is null ? (Start:DateTimeOffset.UtcNow.Date, End:DateTimeOffset.UtcNow.Date.AddDays(1)) : default;
        var dayBounds=_report is null ? (new DateTimeOffset(bounds.Start),new DateTimeOffset(bounds.End)) : ReportBuilder.Bounds(_report.Date,TimeZoneInfo.Local);
        var dayHours=(dayBounds.Item2-dayBounds.Item1).TotalHours;
        for(var hour=0d;hour<=dayHours;hour=Math.Min(hour+3,dayHours))
        {
            var pos=x+width*hour/dayHours;
            dc.DrawLine(new Pen(ThemeManager.Get("Border"),1),new(pos,69),new(pos,74));
            var text=hour==dayHours?"24:00":TimeZoneInfo.ConvertTime(dayBounds.Item1.AddHours(hour),TimeZoneInfo.Local).ToString("HH:mm");
            var label=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),10,ThemeManager.Get("Muted"),VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(label,new(Math.Clamp(pos-label.Width/2,0,Math.Max(0,ActualWidth-label.Width)),79));
            if(hour==dayHours) break;
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if(_report is null) return;
        var (a,b)=ReportBuilder.Bounds(_report.Date,TimeZoneInfo.Local);
        var fraction=Math.Clamp((e.GetPosition(this).X-2)/Math.Max(1,ActualWidth-4),0,1);
        var time=a+TimeSpan.FromTicks((long)((b-a).Ticks*fraction));
        var entry=_report.Timeline.FirstOrDefault(x=>x.Start<=time&&time<x.End);
        ToolTip=entry is null?$"{time.LocalDateTime:HH:mm} · 未记录":$"{entry.Label}{(entry.Kind=="idle"?" · 空闲":"")}\n{entry.Start.LocalDateTime:HH:mm:ss} – {entry.End.LocalDateTime:HH:mm:ss}\n{MainWindow.FormatTime(entry.End-entry.Start)}";
    }
}
