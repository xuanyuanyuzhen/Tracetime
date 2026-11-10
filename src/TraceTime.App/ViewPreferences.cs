using System;
using System.IO;
using System.Text.Json;

namespace TraceTime.App;

// Navigation preferences are independent of themes and usage records.
internal sealed record ViewPreferences(bool SidebarCollapsed = false, string DayChart = "bar", int TemporaryMinutes = 10)
{
    internal ViewPreferences Normalize() => this with { DayChart = DayChart is "bar" or "timeline" or "ring" ? DayChart : "bar", TemporaryMinutes = TemporaryMinutes is >=1 and <=120 ? TemporaryMinutes : 10 };

    internal static ViewPreferences Load(string path)
    {
        try { return (JsonSerializer.Deserialize<ViewPreferences>(File.ReadAllText(path)) ?? new()).Normalize(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(Normalize(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
