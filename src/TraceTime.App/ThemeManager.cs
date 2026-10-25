using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace TraceTime.App;

internal static class ThemeManager
{
    internal static AppearanceSettings Current { get; private set; } = new();
    internal static bool IsDark { get; private set; }
    internal static Brush Get(string key) => (Brush)Application.Current.Resources[key];
    private static Color ColorOf(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static Color Mix(Color a, Color b, double amount) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * amount), (byte)Math.Round(a.G + (b.G - a.G) * amount), (byte)Math.Round(a.B + (b.B - a.B) * amount));

    internal static bool SystemIsDark()
    {
        try { using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"); return key?.GetValue("AppsUseLightTheme") is int value && value == 0; }
        catch { return false; }
    }

    internal static void Apply(AppearanceSettings settings, Window? window = null)
    {
        Current = settings.Normalize();
        IsDark = Current.Mode == "dark" || (Current.Mode == "system" && SystemIsDark());
        var dark = IsDark;
        var highContrast = SystemParameters.HighContrast;
        var glass = Current.Material == "glass" && !highContrast;
        var soft = Current.Material == "soft";
        var resources = Application.Current.Resources;
        var canvas = ColorOf(dark ? "#11171D" : soft ? "#F3F4F6" : "#F5F7F9");
        var surface = ColorOf(dark ? "#1C252E" : "#FFFFFF");
        var ink = ColorOf(dark ? "#F1F5F8" : "#202D39");
        var accent = ColorOf(Current.Accent);
        // Keep arbitrary user colors usable for text and small controls.
        var target = dark ? Colors.White : Colors.Black;
        for (var step = 0; step < 80 && Contrast(accent, surface) < 4.8; step++) accent = Mix(accent, target, .06);
        void Set(string key, Color value) { var brush = new SolidColorBrush(value); brush.Freeze(); resources[key] = brush; }
        void Hex(string key, string value) => Set(key, ColorOf(value));
        Set("Canvas", canvas); Set("Ink", ink); Set("Accent", accent);
        Set("AccentFill", accent); Set("OnAccent", Contrast(accent, Colors.White) >= Contrast(accent, ColorOf("#101820")) ? Colors.White : ColorOf("#101820"));
        Set("Muted", ColorOf(dark ? "#A8B7C5" : "#596A79"));
        Set("Surface", surface); Set("Field", dark ? ColorOf("#18212A") : Colors.White);
        Set("Hover", Mix(surface, accent, dark ? .15 : .08));
        Set("Pressed", Mix(surface, accent, dark ? .23 : .16));
        Set("AccentSoft", Mix(surface, accent, dark ? .16 : .10));
        Set("Divider", ColorOf(dark ? "#34414D" : "#DEE5EA"));
        Set("Border", ColorOf(dark ? "#475867" : "#BDCBD5"));
        Set("Track", ColorOf(dark ? "#121A22" : "#EAF0F4"));
        Set("Idle", ColorOf(dark ? "#768896" : "#BBCAD3"));
        Set("Gap", ColorOf(dark ? "#465662" : "#D8E0E5"));
        Hex("Warning", dark ? "#F2C078" : "#84530C");
        Hex("WarningSurface", dark ? "#3E3220" : "#FFF2DA");
        Hex("Danger", dark ? "#FFAAA3" : "#AB3636");
        if (glass)
        {
            // Layered, translucent WPF material. No desktop capture or unsupported backdrop APIs.
            var background = new LinearGradientBrush(); background.StartPoint = new(0, 0); background.EndPoint = new(1, 1);
            background.GradientStops.Add(new(Mix(canvas, accent, dark ? .22 : .19), 0));
            background.GradientStops.Add(new(canvas, .55));
            background.GradientStops.Add(new(Mix(canvas, accent, .12), 1)); background.Freeze(); resources["Canvas"] = background;
            var panel = new LinearGradientBrush(); panel.StartPoint = new(0, 0); panel.EndPoint = new(1, 1);
            panel.GradientStops.Add(new(Color.FromArgb(dark ? (byte)237 : (byte)242, surface.R, surface.G, surface.B), 0));
            panel.GradientStops.Add(new(Color.FromArgb(dark ? (byte)219 : (byte)201, surface.R, surface.G, surface.B), 1)); panel.Freeze(); resources["Panel"] = panel;
            Set("Sidebar", Mix(surface, accent, dark ? .04 : .03));
            Set("Rim", dark ? Mix(surface, Colors.White, .20) : Colors.White);
        }
        else { resources["Panel"] = resources["Surface"]; resources["Sidebar"] = resources["Surface"]; resources["Rim"] = resources["Divider"]; }
        resources["PanelRadius"] = new CornerRadius(Current.Material == "minimal" ? 6 : 16);
        resources["ControlRadius"] = new CornerRadius(Current.Material == "minimal" ? 5 : 9);
        resources["PanelThickness"] = new Thickness(soft ? 0 : 1);
        resources["PanelPadding"] = new Thickness(22);
        resources["GlassVisibility"] = glass ? Visibility.Visible : Visibility.Collapsed;
        if (highContrast)
        {
            foreach (var key in new[] { "Canvas", "Surface", "Panel", "Sidebar", "Field", "Track" }) resources[key] = SystemColors.WindowBrush;
            foreach (var key in new[] { "Ink", "Muted", "Border", "Divider", "Rim", "Idle", "Gap", "Warning", "Danger" }) resources[key] = SystemColors.WindowTextBrush;
            foreach (var key in new[] { "Accent", "AccentFill" }) resources[key] = SystemColors.HotTrackBrush;
            resources["OnAccent"] = SystemColors.WindowBrush;
            foreach (var key in new[] { "Hover", "Pressed", "AccentSoft", "WarningSurface" }) resources[key] = SystemColors.WindowBrush;
        }
        if (window is not null) ApplyTitleBar(window);
    }

    internal static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
        var color = ((SolidColorBrush)Get("Sidebar")).Color;
        var caption = color.R | (color.G << 8) | (color.B << 16);
        DwmSetWindowAttribute(handle, 35, ref caption, sizeof(int));
        var text = ((SolidColorBrush)Get("Ink")).Color;
        var textColor = text.R | (text.G << 8) | (text.B << 16);
        DwmSetWindowAttribute(handle, 36, ref textColor, sizeof(int));
    }

    internal static double Contrast(Color a, Color b)
    {
        static double L(Color c)
        {
            static double S(byte v) { var x = v / 255d; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
            return .2126 * S(c.R) + .7152 * S(c.G) + .0722 * S(c.B);
        }
        var x = L(a); var y = L(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
