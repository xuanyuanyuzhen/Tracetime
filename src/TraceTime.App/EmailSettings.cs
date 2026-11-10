using System;
using System.IO;
using System.Text;
using System.Text.Json;
using TraceTime.Core;

namespace TraceTime.App;

internal sealed record EmailSettings(bool Enabled = false, string Host = "", int Port = 587,
    string From = "", string To = "", string UserName = "", string ProtectedPassword = "")
{
    internal EmailConnection Connection => new(Host, Port, From, To, UserName);
    internal static EmailSettings Load(string path)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<EmailSettings>(File.ReadAllText(path));
            return settings is null || settings.Host is null || settings.From is null || settings.To is null || settings.UserName is null || settings.ProtectedPassword is null ? new() : settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }
    internal string Password() => string.IsNullOrEmpty(ProtectedPassword) ? "" : Encoding.UTF8.GetString(UserSecret.Unprotect(Convert.FromBase64String(ProtectedPassword)));
    internal EmailSettings WithPassword(string password) => this with { ProtectedPassword = Convert.ToBase64String(UserSecret.Protect(Encoding.UTF8.GetBytes(password))) };
    internal bool SameAccount(EmailSettings other) => string.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase)
        && Port == other.Port && string.Equals(UserName, other.UserName, StringComparison.Ordinal) && From == other.From;
    internal void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
