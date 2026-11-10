using System.Text.Json;

namespace TraceTime.Core;

public sealed record BreakReminderSettings(bool Enabled = false, int Minutes = 30, ReminderLevel Level = ReminderLevel.Medium, DateOnly? IgnoredOn = null)
{
    public void Validate()
    {
        if (Minutes is < 5 or > 240 || !Enum.IsDefined(Level))
            throw new ArgumentException("提醒间隔须为5–240之间的整数分钟，并选择有效强度。");
    }
    public static BreakReminderSettings Load(string path)
    {
        try { var settings = JsonSerializer.Deserialize<BreakReminderSettings>(File.ReadAllText(path)) ?? new(); settings.Validate(); return settings; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return new(); }
    }
    public void Save(string path)
    {
        Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}

public enum ComputerUseState { Using, Paused, Away }

// No Windows or wall-clock dependencies. Input inactivity is deliberately not a rest signal.
public sealed class BreakReminderClock
{
    public static readonly TimeSpan RestDuration = TimeSpan.FromMinutes(5);
    private TimeSpan? _last;
    private ComputerUseState _previous;
    private TimeSpan _away;
    public TimeSpan Used { get; private set; }
    public TimeSpan NextReminder { get; private set; }
    public bool Enabled { get; private set; }
    public bool ManualRest { get; private set; }
    public TimeSpan RestElapsed => _away;
    public bool Pending { get; private set; }
    private TimeSpan _interval = TimeSpan.FromMinutes(30);

    public void Configure(bool enabled, TimeSpan interval, TimeSpan now, ComputerUseState state)
    {
        if (interval < TimeSpan.FromMinutes(5) || interval > TimeSpan.FromMinutes(240)) throw new ArgumentOutOfRangeException(nameof(interval));
        Enabled = enabled; _interval = interval; Used = _away = TimeSpan.Zero;
        NextReminder = interval; ManualRest = Pending = false; _last = now; _previous = state;
    }

    public void Observe(TimeSpan now, ComputerUseState state)
    {
        var next = ManualRest ? ComputerUseState.Away : state;
        if (_last is TimeSpan last && Enabled)
        {
            var delta = now - last;
            if (delta < TimeSpan.Zero) { _away = TimeSpan.Zero; }
            else if (_previous == ComputerUseState.Away)
            {
                _away += delta;
                if (_away >= RestDuration) { Used = TimeSpan.Zero; NextReminder = _interval; Pending = false; }
            }
            // Match the collector's gap guard; unknown outages neither count as use nor prove a rest.
            else if (_previous == ComputerUseState.Using && delta <= TimeSpan.FromSeconds(15)) Used += delta;
        }
        if (next != ComputerUseState.Away) _away = TimeSpan.Zero;
        _previous = next; _last = now;
    }

    public bool IsDue(bool canPresent) => Enabled && !ManualRest && _previous == ComputerUseState.Using && canPresent && !Pending && Used >= NextReminder;
    public void Presented() => Pending = true;
    public void ReleasePresentation() => Pending = false;
    public void Snooze() { Pending = false; NextReminder = Used + TimeSpan.FromMinutes(5); }
    public void NotificationShown() { Pending = false; NextReminder = Used + _interval; }
    public void BeginRest(TimeSpan now, ComputerUseState state) { Observe(now, state); ManualRest = true; Pending = false; _away = TimeSpan.Zero; _previous = ComputerUseState.Away; }
    public void Resume(TimeSpan now, ComputerUseState state) { Observe(now, state); ManualRest = false; Observe(now, state); }
}
