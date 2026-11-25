using System;
using System.Linq;

namespace TraceTime.App.Windows;

internal sealed record StartupEntry(string? Command, bool IsString = true);
internal sealed record StartupExecutable(bool Exists, Version? Version);
internal enum StartupMigration { Skipped, Disabled, Current, Updated, Unrecognized, NewerVersion }

internal interface IStartupEntryStore
{
    StartupEntry? Read();
    void Replace(StartupEntry? expected, StartupEntry? value);
}

// Registry access and executable metadata are injected so upgrade behavior can be
// verified without enabling startup, launching another app or changing user data.
internal sealed class StartupPreferences(IStartupEntryStore store, string executable, Version version,
    Func<string, StartupExecutable> inspectExecutable)
{
    internal bool IsEnabled => TryGetExecutable(store.Read(), out _);
    internal bool PointsElsewhere => TryGetExecutable(store.Read(), out var path)
        && !string.Equals(path, executable, StringComparison.OrdinalIgnoreCase);

    internal static bool IsNormalLaunch(string[] arguments, bool packaged) =>
        packaged && arguments.All(x => x == "--tray");

    internal StartupMigration Synchronize(bool eligible = true)
    {
        if (!eligible) return StartupMigration.Skipped;
        var previous = store.Read();
        if (previous is null) return StartupMigration.Disabled;
        if (!TryGetExecutable(previous, out var oldPath)) return StartupMigration.Unrecognized;
        if (string.Equals(oldPath, executable, StringComparison.OrdinalIgnoreCase)) return StartupMigration.Current;
        var old = inspectExecutable(oldPath);
        if (old.Exists && old.Version is null) return StartupMigration.Unrecognized;
        if (old.Version is { } oldVersion && oldVersion > version) return StartupMigration.NewerVersion;
        store.Replace(previous, CurrentEntry());
        return StartupMigration.Updated;
    }

    internal void Save(bool enabled, Action savePreferences)
    {
        var previous = store.Read();
        if (previous is not null && !TryGetExecutable(previous, out _))
            throw new InvalidOperationException("已有同名启动项无法识别，未修改。请在 Windows 启动应用中检查 TraceTime。");
        var desired = enabled ? CurrentEntry() : null;
        var changed = previous != desired;
        if (changed) store.Replace(previous, desired);
        try { savePreferences(); }
        catch (Exception saveError)
        {
            if (changed)
            {
                try { store.Replace(desired, previous); }
                catch (Exception restoreError)
                {
                    throw new InvalidOperationException("记录偏好未保存，启动项也未能恢复。请重新检查开机启动设置。",
                        new AggregateException(saveError, restoreError));
                }
            }
            throw;
        }
    }

    private StartupEntry CurrentEntry()
    {
        var entry = new StartupEntry($"\"{executable}\" --tray");
        if (!TryGetExecutable(entry, out _) || entry.Command!.Length > 260)
            throw new InvalidOperationException("程序路径无法用于开机启动。请将发布包解压到较短的固定路径，再运行 TraceTime.exe。");
        return entry;
    }

    internal static bool TryGetExecutable(StartupEntry? entry, out string path)
    {
        path = "";
        if (entry is not { IsString: true, Command: { } raw }) return false;
        var command = raw.Trim();
        if (!command.StartsWith('"')) return false;
        var end = command.IndexOf('"', 1);
        if (end < 1 || command[(end + 1)..].Trim() != "--tray") return false;
        var candidate = command[1..end];
        // Accept only the exact command shape TraceTime has written historically.
        var drivePath = candidate.Length > 3 && char.IsAsciiLetter(candidate[0]) && candidate[1] == ':' && candidate[2] == '\\';
        var uncPath = candidate.StartsWith(@"\\") && candidate[2..].Split('\\').Length >= 3;
        if ((!drivePath && !uncPath) || candidate.Any(char.IsControl)
            || !candidate.EndsWith(@"\TraceTime.exe", StringComparison.OrdinalIgnoreCase)) return false;
        path = candidate;
        return true;
    }
}
