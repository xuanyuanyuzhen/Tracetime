using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TraceTime.App;

// Drawn charts share one lazily-created popup each. Moving between regions updates it in place.
public abstract class HoverChart : FrameworkElement
{
    private readonly List<(Geometry Shape, string Text)> _regions = [];
    private ToolTip? _popup;
    private TextBlock? _content;
    private Window? _window;
    private ScrollViewer? _scroll;
    private int _keyboardRegion = -1;
    private Point _hoverPoint;
    internal IReadOnlyList<(Geometry Shape, string Text)> HoverRegions => _regions;
    internal string? HoverText { get; private set; }
    internal bool IsHoverOpen => _popup?.IsOpen == true;
    internal FrameworkElement? HoverPopup => _popup;

    protected HoverChart()
    {
        Focusable = true;
        SetResourceReference(FocusVisualStyleProperty, "KeyboardFocusVisual");
        Loaded += (_, _) => AttachDismissEvents();
        Unloaded += (_, _) => DetachDismissEvents();
        IsVisibleChanged += (_, _) => { if (!IsVisible) CloseHover(); };
    }

    protected sealed override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        _regions.Clear();
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        RenderChart(dc);
        // Refresh the displayed value in place when a timer redraws the chart under a still pointer.
        if (!IsHoverOpen) return;
        if (_keyboardRegion >= 0)
        {
            if (_keyboardRegion >= _regions.Count) { CloseHover(); return; }
            var region = _regions[_keyboardRegion];
            ShowHover(region.Text, new(region.Shape.Bounds.Left, region.Shape.Bounds.Top));
        }
        else UpdateHoverAt(_hoverPoint);
    }

    protected abstract void RenderChart(DrawingContext dc);

    protected void AddHoverRegion(Rect bounds, string text) => AddHoverRegion(new RectangleGeometry(bounds), text);
    protected void AddHoverRegion(Geometry shape, string text)
    {
        if (shape.CanFreeze) shape.Freeze();
        _regions.Add((shape, text));
    }

    protected void ResetHover() { CloseHover(); _regions.Clear(); _keyboardRegion = -1; }

    internal void UpdateHoverAt(Point point)
    {
        _keyboardRegion = -1;
        var match = _regions.LastOrDefault(region => region.Shape.FillContains(point));
        if (match.Shape is null) { CloseHover(); return; }
        ShowHover(match.Text, point);
    }

    private void ShowHover(string text, Point point)
    {
        if (!IsVisible) return;
        if (_popup is null)
        {
            _content = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 340, FontSize = 12 };
            _popup = new ToolTip { Content = _content, PlacementTarget = this, Placement = PlacementMode.Relative, StaysOpen = true, IsHitTestVisible = false };
            _popup.SetResourceReference(StyleProperty, typeof(ToolTip));
        }
        if (HoverText != text) _content!.Text = text;
        HoverText = text;
        _hoverPoint = point;
        _popup.HorizontalOffset = point.X + 14;
        _popup.VerticalOffset = point.Y + 18;
        _popup.IsOpen = true;
    }

    internal void CloseHover()
    {
        if (_popup is not null) _popup.IsOpen = false;
        HoverText = null;
    }

    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); UpdateHoverAt(e.GetPosition(this)); }
    protected override void OnMouseLeave(MouseEventArgs e) { base.OnMouseLeave(e); CloseHover(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); CloseHover(); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape) { CloseHover(); e.Handled = true; return; }
        if (_regions.Count == 0) return;
        var next = e.Key switch
        {
            Key.Right or Key.Down => Math.Min(_regions.Count - 1, _keyboardRegion + 1),
            Key.Left or Key.Up => Math.Max(0, _keyboardRegion - 1),
            Key.Home => 0,
            Key.End => _regions.Count - 1,
            _ => -1
        };
        if (next < 0) return;
        _keyboardRegion = next;
        var region = _regions[next];
        ShowHover(region.Text, new(region.Shape.Bounds.Left, region.Shape.Bounds.Top));
        e.Handled = true;
    }

    private void AttachDismissEvents()
    {
        DetachDismissEvents();
        _window = Window.GetWindow(this);
        if (_window is not null) _window.Deactivated += Dismiss;
        for (DependencyObject? parent = VisualTreeHelper.GetParent(this); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer scroll) { _scroll = scroll; _scroll.ScrollChanged += OnScroll; break; }
    }
    private void DetachDismissEvents()
    {
        CloseHover();
        if (_window is not null) _window.Deactivated -= Dismiss;
        if (_scroll is not null) _scroll.ScrollChanged -= OnScroll;
        _window = null; _scroll = null;
    }
    private void Dismiss(object? sender, EventArgs e) => CloseHover();
    private void OnScroll(object sender, ScrollChangedEventArgs e) => CloseHover();
}

internal static class ChartDetails
{
    internal static string Duration(TimeSpan value)
    {
        var seconds = Math.Max(0, value.Ticks / TimeSpan.TicksPerSecond);
        if (seconds >= 3600) return $"{seconds / 3600} 小时 {seconds / 60 % 60} 分 {seconds % 60} 秒";
        return seconds >= 60 ? $"{seconds / 60} 分 {seconds % 60} 秒" : $"{seconds} 秒";
    }

    internal static Geometry Sector(Point center, double outer, double inner, double start, double sweep)
    {
        if (sweep >= 359.999999)
        {
            var circle = new EllipseGeometry(center, outer, outer);
            return inner <= 0 ? circle : new CombinedGeometry(GeometryCombineMode.Exclude, circle, new EllipseGeometry(center, inner, inner));
        }
        Point At(double radius, double angle) => new(center.X + radius * Math.Cos(angle * Math.PI / 180), center.Y + radius * Math.Sin(angle * Math.PI / 180));
        var figure = new PathFigure { StartPoint = At(outer, start), IsClosed = true, IsFilled = true };
        figure.Segments.Add(new ArcSegment(At(outer, start + sweep), new(outer, outer), 0, sweep > 180, SweepDirection.Clockwise, true));
        figure.Segments.Add(new LineSegment(inner > 0 ? At(inner, start + sweep) : center, true));
        if (inner > 0) figure.Segments.Add(new ArcSegment(At(inner, start), new(inner, inner), 0, sweep > 180, SweepDirection.Counterclockwise, true));
        return new PathGeometry([figure]);
    }
}
