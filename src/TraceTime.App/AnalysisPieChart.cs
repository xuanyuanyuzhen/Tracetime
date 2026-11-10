using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using TraceTime.Core;

namespace TraceTime.App;

public sealed class AnalysisPieChart : HoverChart
{
    private IReadOnlyList<UsageShare> _shares = [];
    private bool _withoutIdle;
    private TimeSpan _total;
    private string _scope = "";

    internal void SetData(IReadOnlyList<UsageShare> shares, bool withoutIdle, TimeSpan total, string scope)
    {
        _shares = shares; _withoutIdle = withoutIdle; _total = total; _scope = scope;
        InvalidateVisual();
    }

    protected override void RenderChart(DrawingContext dc)
    {
        var radius = Math.Min(98, ActualWidth * .19);
        var center = new Point(radius + 12, ActualHeight / 2);
        if (_shares.Count == 0 || _total <= TimeSpan.Zero)
        {
            dc.DrawEllipse(ThemeManager.Get("Track"), null, center, radius, radius);
            Text(dc, "暂无时长", center.X - 28, center.Y - 8, 14, ThemeManager.Get("Muted"));
            Text(dc, "当前口径暂无可显示的应用时长。", 2 * radius + 48, center.Y - 9, 12, ThemeManager.Get("Muted"), Math.Max(1, ActualWidth - 2 * radius - 48));
            return;
        }

        var keys = _shares.Where(x => !x.IsOther).Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var angle = -90d;
        var legendX = 2 * radius + 44;
        var legendY = (ActualHeight - _shares.Count * 42) / 2;
        for (var i = 0; i < _shares.Count; i++)
        {
            var share = _shares[i];
            var fraction = share.Duration(_withoutIdle).Ticks / (double)_total.Ticks;
            var color = share.IsOther ? ThemeManager.Get("Muted") : Palette.At(Array.IndexOf(keys, share.Key));
            var sector = ChartDetails.Sector(center, radius, 0, angle, fraction * 360);
            dc.DrawGeometry(color, new Pen(ThemeManager.Get("Surface"), 1.5), sector);
            var metric = _withoutIdle ? "扣除空闲后" : "前台停留";
            var detail = $"{share.Name}\n{_scope}\n前台停留：{ChartDetails.Duration(share.Foreground)}\n扣除空闲后：{ChartDetails.Duration(share.WithoutIdle)}\n{metric}占比：{fraction:P1}\n分母：所选日期全部应用的{metric} {ChartDetails.Duration(_total)}";
            AddHoverRegion(sector, detail);
            angle += fraction * 360;

            var y = legendY + i * 42;
            dc.DrawRoundedRectangle(color, null, new Rect(legendX, y + 4, 10, 10), 2, 2);
            Text(dc, share.Name, legendX + 18, y, 12, ThemeManager.Get("Ink"), Math.Max(1, ActualWidth - legendX - 80));
            Text(dc, fraction.ToString("P1"), ActualWidth - 54, y, 12, ThemeManager.Get("Muted"));
            Text(dc, ChartDetails.Duration(share.Duration(_withoutIdle)), legendX + 18, y + 19, 12, ThemeManager.Get("Muted"), Math.Max(1, ActualWidth - legendX - 20));
            AddHoverRegion(new Rect(legendX, y, Math.Max(1, ActualWidth - legendX), 37), detail);
        }
    }

    private void Text(DrawingContext dc, string text, double x, double y, int size, Brush brush, double? width = null)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (width is double limit) { formatted.MaxTextWidth = limit; formatted.MaxLineCount = 1; formatted.Trimming = TextTrimming.CharacterEllipsis; }
        dc.DrawText(formatted, new Point(x, y));
    }
}
