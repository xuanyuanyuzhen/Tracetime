using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TraceTime.App;

// Independent of both Windows and usage records; also compiled into the scenario runner.
internal sealed record AppearanceSettings(string Mode = "system", string Material = "glass", string Accent = "#24777B")
{
    internal static readonly string[] Modes = ["system", "light", "dark"];
    internal static readonly string[] Materials = ["glass", "soft", "minimal"];
    internal static bool ValidAccent(string? value) => value is not null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant);
    internal AppearanceSettings Normalize() => new(
        Array.IndexOf(Modes, Mode) >= 0 ? Mode : "system",
        Array.IndexOf(Materials, Material) >= 0 ? Material : "glass",
        ValidAccent(Accent) ? Accent.ToUpperInvariant() : "#24777B");

    internal static AppearanceSettings Load(string path)
    {
        try { return (JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path)) ?? new()).Normalize(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(Normalize(), new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
    }
}
