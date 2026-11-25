using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace TraceTime.App.Windows;

internal static class StartupRegistration
{
    internal static StartupPreferences? ForLaunch(string[] arguments)
    {
        if (!StartupPreferences.IsNormalLaunch(arguments, File.Exists(Path.Combine(AppContext.BaseDirectory, "PACKAGE-TYPE.txt")))) return null;
        var path = Environment.ProcessPath;
        if (path is null || !string.Equals(Path.GetFileName(path), "TraceTime.exe", StringComparison.OrdinalIgnoreCase)) return null;
        return new(new RegistryStartupStore(), path, typeof(App).Assembly.GetName().Version!, InspectExecutable);
    }

    private static StartupExecutable InspectExecutable(string path)
    {
        if (!File.Exists(path)) return new(false, null);
        var info = FileVersionInfo.GetVersionInfo(path);
        if (info.ProductName != "TraceTime" || info.OriginalFilename != "TraceTime.dll") return new(true, null);
        return new(true, new(info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart));
    }
}

internal sealed class RegistryStartupStore : IStartupEntryStore
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public StartupEntry? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return Read(key);
    }
    private static StartupEntry? Read(RegistryKey? key)
    {
        var raw = key?.GetValue("TraceTime", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw is null) return null;
        return new(raw as string, key!.GetValueKind("TraceTime") == RegistryValueKind.String);
    }
    public void Replace(StartupEntry? expected, StartupEntry? value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (Read(key) != expected) throw new InvalidOperationException("启动项已被其他操作更改，请重新打开设置后再保存。");
        if (value is null) key.DeleteValue("TraceTime", false);
        else key.SetValue("TraceTime", value.Command!, RegistryValueKind.String);
        // Do not modify StartupApproved: Windows-level disabled state belongs to the user.
    }
}
