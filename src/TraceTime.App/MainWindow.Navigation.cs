using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TraceTime.App;

public partial class MainWindow
{
    private readonly string _viewPreferencesPath;
    private ViewPreferences _viewPreferences;
    private bool _syncingViewPreferences;

    private void ApplyViewPreferences()
    {
        _syncingViewPreferences = true;
        try
        {
            var compact = _viewPreferences.SidebarCollapsed;
            ApplyResourceLayout(compact);
            SidebarColumn.Width = new GridLength(compact ? 72 : 200);
            SidebarContent.Margin = new Thickness(compact ? 12 : 16, 28, compact ? 12 : 16, 22);
            BrandPanel.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
            BrandPanel.Margin = new Thickness(compact ? 0 : 8, 0, 0, 18);
            BrandIcon.Margin = new Thickness(0, 0, compact ? 0 : 10, 0);
            foreach (var label in new FrameworkElement[] { BrandLabel, SidebarToggleLabel, SidebarNote, SidebarVersion })
                label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            foreach (var button in new[] { OverviewNav, LimitsNav, BreaksNav, AnalysisNav, AppearanceNav, SettingsNav, QuickThemeButton, SidebarToggle })
            {
                button.HorizontalContentAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
                button.Padding = new Thickness(compact ? 10 : 14, 12, compact ? 10 : 14, 12);
                if (button.Content is not StackPanel content) continue;
                foreach (var label in content.Children.OfType<TextBlock>()) label.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
                foreach (var icon in content.Children.OfType<System.Windows.Shapes.Path>()) icon.Margin = new Thickness(0, 0, compact ? 0 : 12, 0);
            }
            SidebarToggleIcon.Data = Geometry.Parse(compact ? "M2,2 L8,8 L2,14 M9,2 L15,8 L9,14" : "M8,2 L2,8 L8,14 M15,2 L9,8 L15,14");
            SidebarToggle.ToolTip = compact ? "展开侧边栏" : "收起侧边栏";
            AutomationProperties.SetName(SidebarToggle, (string)SidebarToggle.ToolTip);
            ActivityBar.IsChecked = _viewPreferences.DayChart == "bar";
            ActivityLine.IsChecked = _viewPreferences.DayChart == "timeline";
            ActivityRing.IsChecked = _viewPreferences.DayChart == "ring";
            Timeline.Mode = _viewPreferences.DayChart;
            ActivityLegend.Visibility = _viewPreferences.DayChart=="ring" ? Visibility.Collapsed : Visibility.Visible;
            ActivityBackgroundHint.Text = _viewPreferences.DayChart=="timeline" ? "虚线后：尚未到来" : "浅底：尚未到来";
        }
        finally { _syncingViewPreferences = false; }
    }

    private void SaveViewPreferences(ViewPreferences value)
    {
        try
        {
            value.Normalize().Save(_viewPreferencesPath);
            _viewPreferences = value.Normalize();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { ShowError("界面偏好未保存，请检查数据文件夹权限：" + ex.Message); }
        ApplyViewPreferences();
    }

    private void ToggleSidebar(object sender, RoutedEventArgs e) =>
        SaveViewPreferences(_viewPreferences with { SidebarCollapsed = !_viewPreferences.SidebarCollapsed });

    private void OverviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // The page owns wheel scrolling. An explicitly targeted scrollbar still works normally.
        for (var node = e.OriginalSource as DependencyObject; node is not null && node != OverviewPanel;)
        {
            if (node is ScrollBar) return;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : (node as FrameworkContentElement)?.Parent;
        }
        var lines = SystemParameters.WheelScrollLines;
        var distance = lines < 0 ? OverviewPanel.ViewportHeight : lines * 16d;
        OverviewPanel.ScrollToVerticalOffset(OverviewPanel.VerticalOffset - e.Delta / 120d * distance);
        e.Handled = true;
    }
}
