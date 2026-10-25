namespace TraceTime.Core;

/// <summary>Single-threaded state machine. Wall-clock discontinuities and observation gaps start a new scope.</summary>
public sealed class TrackingEngine
{
    private readonly Dictionary<string, UsageSegment> _dirtyUsage = new();
    private readonly Dictionary<string, IdleInterval> _dirtyIdle = new();
    private UsageSegment? _usage;
    private IdleInterval? _idle;
    private string _scope = "";
    private long _anchorMono, _lastMono, _lastInput;
    private DateTimeOffset _anchorUtc;
    public long ThresholdMs { get; private set; }
    public bool IsRunning => _usage is not null;
    public bool IsIdle => _idle is not null;
    public DateTimeOffset? LastObservedUtc { get; private set; }

    public TrackingEngine(TimeSpan idleThreshold) => SetThreshold(idleThreshold);

    private void SetThreshold(TimeSpan threshold)
    {
        if (threshold < TimeSpan.FromSeconds(1) || threshold > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(threshold));
        ThresholdMs = (long)threshold.TotalMilliseconds;
    }

    public void Observe(TrackingSample sample)
    {
        if (_usage is null) { Begin(sample); return; }
        var delta = sample.MonotonicMs - _lastMono;
        var expectedUtc = At(sample.MonotonicMs);
        if (delta < 0 || delta > 15_000 || Math.Abs((sample.Utc - expectedUtc).TotalMilliseconds) > 2_000)
        {
            Stop();
            Begin(sample);
            return;
        }

        _usage = _usage with { End = expectedUtc };
        _dirtyUsage[_usage.Id] = _usage;
        var input = Math.Max(_anchorMono, sample.MonotonicMs - Math.Max(0, sample.IdleMs));
        input = Math.Min(input, sample.MonotonicMs);
        if (input > _lastInput)
        {
            // Also catch an idle gap that crossed its threshold and ended between two observations.
            if (input - _lastInput >= ThresholdMs) ExtendIdle(input);
            _idle = null;
            _lastInput = input;
        }
        if (sample.MonotonicMs - _lastInput >= ThresholdMs) ExtendIdle(sample.MonotonicMs);

        if (_usage.App.Key != sample.App.Key)
        {
            _usage = new UsageSegment(Guid.NewGuid().ToString("N"), _scope, sample.App, expectedUtc, expectedUtc, ThresholdMs);
        }
        _lastMono = sample.MonotonicMs;
        LastObservedUtc = expectedUtc;
    }

    private void Begin(TrackingSample sample)
    {
        _scope = Guid.NewGuid().ToString("N");
        _anchorUtc = sample.Utc;
        _anchorMono = _lastMono = _lastInput = sample.MonotonicMs;
        LastObservedUtc = sample.Utc;
        _usage = new UsageSegment(Guid.NewGuid().ToString("N"), _scope, sample.App, sample.Utc, sample.Utc, ThresholdMs);
        _idle = null;
    }

    private DateTimeOffset At(long mono) => _anchorUtc.AddMilliseconds(mono - _anchorMono);

    private void ExtendIdle(long end)
    {
        _idle = _idle is null
            ? new IdleInterval(Guid.NewGuid().ToString("N"), _scope, At(_lastInput), At(end))
            : _idle with { End = At(end) };
        _dirtyIdle[_idle.Id] = _idle;
    }

    // Stop at the last known observation; never extrapolate through unobserved time.
    public void Stop() { _usage = null; _idle = null; }

    public void ChangeThreshold(TimeSpan threshold)
    {
        SetThreshold(threshold);
        Stop();
    }

    public void Flush(ITrackingSink sink)
    {
        if (_dirtyUsage.Count == 0 && _dirtyIdle.Count == 0) return;
        sink.Save(new ChangeBatch(_dirtyUsage.Values.Where(x => x.End > x.Start).ToArray(),
            _dirtyIdle.Values.Where(x => x.End > x.Start).ToArray()));
        // A failing sink leaves the batch intact for a later retry.
        _dirtyUsage.Clear();
        _dirtyIdle.Clear();
    }
}
