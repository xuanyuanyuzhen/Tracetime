using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TraceTime.Core;

namespace TraceTime.App;

public sealed class AnalysisChart : FrameworkElement
{
    private UsageAnalysis? _analysis;
    public UsageAnalysis? Analysis { get => _analysis; set { _analysis = value; InvalidateVisual(); } }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); if (_analysis is null) return;
        var days = _analysis.Days; var w = Math.Max(1, ActualWidth - 38); var h = Math.Max(1, ActualHeight - 54);
        var max = Math.Max(1, days.Max(d => d.Foreground.TotalHours));
        for (int i = 0; i <= 3; i++) { var y = 14 + h * i / 3; dc.DrawLine(new(ThemeManager.Get("Divider"), 1), new(32, y), new(ActualWidth, y)); DrawText(dc, (max * (3 - i) / 3).ToString("0.#"), 0, y - 7, ThemeManager.Get("Muted")); }
        var step = w / days.Count;
        for (int i = 0; i < days.Count; i++)
        {
            var x = 34 + i * step; var width = Math.Max(2, step * .65);
            var fg = h * days[i].Foreground.TotalHours / max; var active = h * days[i].WithoutIdle.TotalHours / max;
            dc.DrawRoundedRectangle(ThemeManager.Get("Border"), null, new(x, 14 + h - fg, width, Math.Max(fg, 1)), 2, 2);
            dc.DrawRoundedRectangle(ThemeManager.Get("Accent"), null, new(x + width * .22, 14 + h - active, width * .56, Math.Max(active, days[i].WithoutIdle.TotalSeconds > 0 ? 1 : 0)), 1, 1);
            if (days.Count <= 7 || i % 5 == 0 || i == days.Count - 1) DrawText(dc, days[i].Date.ToString("M/d"), Math.Min(x, ActualWidth - 34), 21 + h, ThemeManager.Get("Muted"));
        }
        DrawText(dc, "宽条：前台停留    细条：扣除空闲后", 32, ActualHeight - 14, ThemeManager.Get("Muted"));
    }
    private void DrawText(DrawingContext dc, string value, double x, double y, Brush brush) => dc.DrawText(new FormattedText(value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new(x,y));
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e); if (_analysis is null) return;
        var i = (int)Math.Clamp((e.GetPosition(this).X - 34) / Math.Max(1, ActualWidth - 38) * _analysis.Days.Count, 0, _analysis.Days.Count - 1);
        var day = _analysis.Days[i]; ToolTip = $"{day.Date:yyyy-MM-dd}\n前台停留：{MainWindow.FormatTime(day.Foreground)}\n扣除空闲后：{MainWindow.FormatTime(day.WithoutIdle)}";
    }
}
