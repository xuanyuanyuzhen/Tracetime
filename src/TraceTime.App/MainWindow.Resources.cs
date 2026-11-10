using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TraceTime.App;

public partial class MainWindow
{
    private readonly ProcessResourceSampler _resourceSampler = new();
    private DispatcherTimer? _resourceTimer;

    private void InitializeResourceMonitor()
    {
        _resourceTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        { Interval = TimeSpan.FromSeconds(2) };
        _resourceTimer.Tick += ResourceMonitorTick;
        IsVisibleChanged += ResourceVisibilityChanged;
        StateChanged += ResourceWindowStateChanged;
        UpdateResourceMonitoring();
    }

    private void ResourceVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateResourceMonitoring();
    private void ResourceWindowStateChanged(object? sender, EventArgs e) => UpdateResourceMonitoring();
    private void ResourceMonitorTick(object? sender, EventArgs e)
    {
        if (IsVisible && WindowState != WindowState.Minimized) SampleResources();
    }

    private void UpdateResourceMonitoring()
    {
        if (_resourceTimer is null) return;
        var shouldRun = IsVisible && WindowState != WindowState.Minimized;
        if (shouldRun == _resourceTimer.IsEnabled) return;
        _resourceSampler.Reset();
        if (shouldRun) { SampleResources(); _resourceTimer.Start(); }
        else _resourceTimer.Stop();
    }

    private void SampleResources()
    {
        try { RenderResourceSample(_resourceSampler.Sample()); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            _resourceSampler.Reset();
            ResourceCpu.Text = ResourceMemory.Text = "—";
            ResourceUsage.ToolTip = "暂时无法读取 TraceTime 的进程占用，将在下次刷新重试。";
        }
    }

    private void RenderResourceSample(ProcessResourceSample sample)
    {
        ResourceCpu.Text = sample.CpuPercent is double cpu ? cpu.ToString("0.0", CultureInfo.CurrentCulture) + "%" : "—";
        var mib = sample.WorkingSetBytes / 1048576d;
        ResourceMemory.Text = mib >= 1024 ? (mib / 1024).ToString("0.00", CultureInfo.CurrentCulture) + " GiB" : Math.Ceiling(mib).ToString("0", CultureInfo.CurrentCulture) + " MiB";
        ResourceUsage.ToolTip = $"TraceTime 自身进程 · 每 2 秒更新\nCPU：{ResourceCpu.Text}（占全部逻辑处理器容量）\n内存：{ResourceMemory.Text}（当前驻留内存 / 工作集）\n窗口隐藏或最小化时暂停刷新。";
        AutomationProperties.SetName(ResourceUsage, $"TraceTime 资源占用，CPU {ResourceCpu.Text}，内存 {ResourceMemory.Text}");
    }

    private void ApplyResourceLayout(bool compact)
    {
        ResourceUsage.Margin = new Thickness(compact ? 0 : 8, 0, compact ? 0 : 8, 14);
        foreach (var grid in new[] { ResourceCpuRow, ResourceMemoryRow })
        {
            grid.ColumnDefinitions[1].Width = new GridLength(compact ? 0 : 1, GridUnitType.Star);
            var value = (TextBlock)grid.Children[1];
            Grid.SetColumn(value, compact ? 0 : 1);
            Grid.SetRow(value, compact ? 1 : 0);
            value.HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Right;
            ((TextBlock)grid.Children[0]).HorizontalAlignment = compact ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        }
    }

    private void DisposeResourceMonitor()
    {
        _resourceTimer?.Stop();
        if (_resourceTimer is not null) _resourceTimer.Tick -= ResourceMonitorTick;
        IsVisibleChanged -= ResourceVisibilityChanged;
        StateChanged -= ResourceWindowStateChanged;
        _resourceSampler.Dispose();
    }
}
