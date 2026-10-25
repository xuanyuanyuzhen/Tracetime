using System;
using Microsoft.Win32;

namespace TraceTime.App.Windows;

internal static class StartupRegistration
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(Key);
        return string.Equals(key?.GetValue("TraceTime") as string, Command(), StringComparison.OrdinalIgnoreCase);
    }
    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key);
        if (enabled) key.SetValue("TraceTime", Command());
        else key.DeleteValue("TraceTime", false);
    }
    private static string Command() => $"\"{Environment.ProcessPath ?? throw new InvalidOperationException("无法确认程序路径。请运行发布后的 TraceTime.exe。") }\" --tray";
}
