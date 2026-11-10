using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TraceTime.Core;

namespace TraceTime.App;

public sealed class TimelineView : HoverChart
{
    private DayActivity? _activity;
    private string _mode="bar";
    private int _hour=-1;
    public DayActivity? Activity { get=>_activity; set { _activity=value; InvalidateVisual(); } }
    public string Mode { get=>_mode; set { if (_mode == value) return; _mode=value; ResetHover(); InvalidateVisual(); } }
    public int SelectedHour { get=>_hour; set { if (_hour == value) return; _hour=value; ResetHover(); InvalidateVisual(); } }
    public string? SelectedAppKey { get; set; }
    public event Action<int>? HourSelected;
    private Brush Color(string kind) => ThemeManager.Get(kind switch {"active"=>"Accent","idle"=>"Idle","gap"=>"Warning", "unrecorded"=>"Gap",_=>"Track"});
    internal static string KindLabel(string kind)=>kind switch {"active"=>"应用使用（扣除空闲）","idle"=>"空闲（无输入）","gap"=>"睡眠 / 锁屏 / 暂停", "unrecorded"=>"未运行 / 未记录",_=>"尚未到来"};
    protected override void RenderChart(DrawingContext dc)
    {
        if(_activity is null) return;
        if(_mode=="ring") DrawRing(dc); else if(_mode=="timeline") DrawTimeline(dc); else DrawBars(dc);
    }
    private void DrawBars(DrawingContext dc)
    {
        var a=_activity!;double x=30,y=26,w=Math.Max(1,ActualWidth-38),h=Math.Max(1,ActualHeight-76),step=w/a.Hours.Count;
        foreach(var minute in new[]{0,30,60}) {var py=y+h*(1-minute/60d);dc.DrawLine(new(ThemeManager.Get("Divider"),1),new(x,py),new(x+w,py));Text(dc,minute.ToString(),2,py-8,10,ThemeManager.Get("Muted"));}
        Text(dc,"分钟 / 小时",2,3,11,ThemeManager.Get("Muted"));
        foreach(var hour in a.Hours)
        {
            var bx=x+hour.Index*step+step*.12;var bw=Math.Max(2,step*.76);var baseY=y+h;
            if(hour.Index==_hour) dc.DrawRoundedRectangle(ThemeManager.Get("AccentSoft"),new Pen(ThemeManager.Get("Accent"),1),new(bx-2,y-3,bw+4,h+6),3,3);
            foreach(var kind in new[]{"active","idle","gap","unrecorded","future"})
            {
                var seconds=hour.Slice.Entries.Where(e=>e.Kind==kind).Sum(e=>(e.End-e.Start).TotalSeconds);var bh=h*seconds/3600;
                if(bh<=0) continue;baseY-=bh;
                var rect=new Rect(bx,baseY,bw,bh);dc.DrawRectangle(Color(kind),null,rect);
                if(kind is "idle" or "unrecorded") Hatch(dc,rect,kind=="idle"?7:12);
            }
            var label=TimeZoneInfo.ConvertTime(hour.Start,TimeZoneInfo.Local).ToString("HH");
            if(hour.Index%3==0 || hour.Index==a.Hours.Count-1) Text(dc,label,bx,y+h+9,11,ThemeManager.Get("Muted"));
            AddHoverRegion(new Rect(bx,y,bw,h),Describe(hour));
        }
        Text(dc,"点击一列查看该小时；宽窄或纹理仅用于区分状态。",30,ActualHeight-15,10,ThemeManager.Get("Muted"));
    }
    private void DrawTimeline(DrawingContext dc)
    {
        var a=_activity!;double x=6,w=Math.Max(1,ActualWidth-12),h=36;
        string[] kinds=["active","idle","gap","unrecorded"];
        for(int row=0;row<4;row++)
        {
            var y=26+row*43;dc.DrawRoundedRectangle(Color("future"),null,new(x,y,w,h-13),4,4);
            Text(dc,KindLabel(kinds[row]),x,y-16,10,ThemeManager.Get("Muted"));
        }
        foreach(var entry in a.Entries)
        {
            var row=Array.IndexOf(kinds,entry.Kind);if(row<0) continue;
            var left=x+(entry.Start-a.Start).TotalSeconds/(a.End-a.Start).TotalSeconds*w;var right=x+(entry.End-a.Start).TotalSeconds/(a.End-a.Start).TotalSeconds*w;
            var rect=new Rect(left,26+row*43,Math.Max(.8,right-left),23);
            var brush=entry.Kind=="active"?Palette.ForApp(entry.AppKey):Color(entry.Kind);
            dc.PushOpacity(SelectedAppKey is null || string.IsNullOrEmpty(entry.AppKey) || entry.AppKey==SelectedAppKey?1:.25);
            dc.DrawRectangle(brush,null,rect);if(entry.Kind is "idle" or "unrecorded") Hatch(dc,rect,entry.Kind=="idle"?7:12);dc.Pop();
            AddHoverRegion(rect,$"{entry.Label}{(entry.Kind=="idle"?" · 空闲（无输入）":"")}\n{entry.Start.LocalDateTime:HH:mm:ss}–{(entry.End==a.End?"24:00:00":entry.End.LocalDateTime.ToString("HH:mm:ss"))}\n{ChartDetails.Duration(entry.End-entry.Start)}");
        }
        for(int i=0;i<a.Hours.Count;i+=3) {var pos=x+w*i/a.Hours.Count;Text(dc,TimeZoneInfo.ConvertTime(a.Start.AddHours(i),TimeZoneInfo.Local).ToString("HH:mm"),Math.Min(pos,ActualWidth-68),201,10,ThemeManager.Get("Muted"));}
        Text(dc,"24:00",ActualWidth-35,201,10,ThemeManager.Get("Muted"));
        var future=a.Entries.FirstOrDefault(e=>e.Kind=="future");
        if(future is not null)
        {
            var left=x+(future.Start-a.Start).TotalSeconds/(a.End-a.Start).TotalSeconds*w;
            var pen=new Pen(ThemeManager.Get("Muted"),1) {DashStyle=DashStyles.Dash};
            dc.DrawLine(pen,new(left,22),new(left,187));
            Text(dc,"尚未到来 →",Math.Min(left+4,ActualWidth-70),185,10,ThemeManager.Get("Muted"));
        }
        Text(dc,"每行只标出对应状态；点击时间轴选择小时。",6,ActualHeight-14,10,ThemeManager.Get("Muted"));
        if(_hour>=0&&_hour<a.Hours.Count) {var start=x+w*_hour/a.Hours.Count;dc.DrawRectangle(null,new Pen(ThemeManager.Get("Accent"),1.5),new(start,22,w/a.Hours.Count,162));}
    }
    private void DrawRing(DrawingContext dc)
    {
        var a=_activity!;var slice=_hour>=0&&_hour<a.Hours.Count?a.Hours[_hour].Slice:a.Whole;
        var total=(slice.End-slice.Start-slice.Future).TotalSeconds;var radius=Math.Min(75,ActualWidth*.18);var cx=radius+20;var cy=ActualHeight/2-6;
        dc.DrawEllipse(null,new Pen(Color("future"),24),new(cx,cy),radius,radius);
        var values=new[]{("active",slice.Active), ("idle",slice.Idle),("gap",slice.Interrupted),("unrecorded",slice.Unrecorded)};
        var angle=-90d;int row=0;
        foreach(var (kind,duration) in values)
        {
            var share=total<=0?0:duration.TotalSeconds/total;
            var detail=$"{(_hour>=0?a.Hours[_hour].Label:a.Start.LocalDateTime.ToString("yyyy-MM-dd")+" · 全天")}\n{KindLabel(kind)}：{ChartDetails.Duration(duration)}\n占已过去时间：{share:P1}\n已过去合计：{ChartDetails.Duration(TimeSpan.FromSeconds(Math.Max(0,total)))}（不含未来）";
            if(share>0)
            {
                var sector=ChartDetails.Sector(new(cx,cy),radius+12,radius-12,angle,share*360);
                dc.DrawGeometry(Color(kind),null,sector);AddHoverRegion(sector,detail);angle+=share*360;
            }
            var lx=2*radius+58;var ly=22+row*43;dc.DrawRoundedRectangle(Color(kind),null,new(lx,ly+3,10,10),2,2);
            Text(dc,KindLabel(kind),lx+17,ly,11,ThemeManager.Get("Ink"));Text(dc,$"{MainWindow.FormatTime(duration)} · {share:P1}",lx+17,ly+17,11,ThemeManager.Get("Muted"));
            AddHoverRegion(new Rect(lx,ly,Math.Max(1,ActualWidth-lx),36),detail);row++;
        }
        Text(dc,"已过去",cx-22,cy-18,12,ThemeManager.Get("Muted"));Text(dc,$"{total/3600:0.#} 小时",cx-30,cy+4,16,ThemeManager.Get("Ink"));
        Text(dc,$"不含尚未到来的 {MainWindow.FormatTime(slice.Future)}；下方可切换时段。",6,ActualHeight-15,10,ThemeManager.Get("Muted"));
    }
    private void Hatch(DrawingContext dc,Rect rect,int step)
    {
        dc.PushClip(new RectangleGeometry(rect));var pen=new Pen(ThemeManager.Get("Muted"),.65);dc.PushOpacity(.35);
        for(double x=rect.Left-rect.Height;x<rect.Right;x+=step) dc.DrawLine(pen,new(x,rect.Bottom),new(x+rect.Height,rect.Top));dc.Pop();dc.Pop();
    }
    private void Text(DrawingContext dc,string text,double x,double y,int size,Brush brush)=>dc.DrawText(new FormattedText(text,CultureInfo.CurrentCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),size,brush,VisualTreeHelper.GetDpi(this).PixelsPerDip),new(x,y));
    private static string Describe(ActivityHour hour)=>$"{hour.Label}\n前台停留：{ChartDetails.Duration(hour.Slice.Foreground)}\n应用使用（扣除空闲）：{ChartDetails.Duration(hour.Slice.Active)}\n空闲（无输入）：{ChartDetails.Duration(hour.Slice.Idle)}\n已知中断：{ChartDetails.Duration(hour.Slice.Interrupted)}\n未运行 / 未记录：{ChartDetails.Duration(hour.Slice.Unrecorded)}\n尚未到来：{ChartDetails.Duration(hour.Slice.Future)}";
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);if(_activity is null||_mode=="ring")return;Focus();
        var p=e.GetPosition(this);var hour=_mode=="bar"?(int)((p.X-30)/Math.Max(1,ActualWidth-38)*_activity.Hours.Count):(int)((p.X-6)/Math.Max(1,ActualWidth-12)*_activity.Hours.Count);
        HourSelected?.Invoke(Math.Clamp(hour,0,_activity.Hours.Count-1));e.Handled=true;
    }
    protected override void OnKeyDown(KeyEventArgs e){if(_mode=="ring"){base.OnKeyDown(e);return;}if(_activity is null)return;var next=e.Key switch {Key.Left=>Math.Max(0,_hour-1),Key.Right=>Math.Min(_activity.Hours.Count-1,_hour+1),Key.Escape=>-1,_=>int.MinValue};if(next!=int.MinValue){CloseHover();HourSelected?.Invoke(next);e.Handled=true;}else base.OnKeyDown(e);}
}
