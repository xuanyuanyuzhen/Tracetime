using System;
using System.Diagnostics;

namespace TraceTime.App;

internal readonly record struct ProcessResourceSample(double? CpuPercent, long WorkingSetBytes);

// One process handle and two cheap counters; no WMI, process enumeration or database writes.
internal sealed class ProcessResourceSampler : IDisposable
{
    private readonly Process _process = Process.GetCurrentProcess();
    private long? _previousTimestamp;
    private TimeSpan _previousCpu;

    internal ProcessResourceSample Sample()
    {
        _process.Refresh();
        var timestamp = Stopwatch.GetTimestamp();
        var cpu = _process.TotalProcessorTime;
        var percent = _previousTimestamp is long previous
            ? CalculateCpuPercent(cpu - _previousCpu, Stopwatch.GetElapsedTime(previous, timestamp), Environment.ProcessorCount)
            : (double?)null;
        _previousTimestamp = timestamp;
        _previousCpu = cpu;
        return new(percent, _process.WorkingSet64);
    }

    internal static double? CalculateCpuPercent(TimeSpan cpu, TimeSpan elapsed, int processors)
    {
        if (elapsed <= TimeSpan.Zero || cpu < TimeSpan.Zero || processors < 1) return null;
        return Math.Clamp(cpu.TotalSeconds / elapsed.TotalSeconds / processors * 100, 0, 100);
    }

    internal void Reset() => _previousTimestamp = null;
    public void Dispose() => _process.Dispose();
}
