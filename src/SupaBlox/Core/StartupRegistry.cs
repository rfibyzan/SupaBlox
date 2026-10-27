using Microsoft.Win32;

namespace SupaBlox.Core;

/// <summary>Mulai bersama Windows lewat HKCU\...\Run (tanpa admin).</summary>
public static class StartupRegistry
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "SupaBlox";

    public static bool IsEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(Name) is string;
    }

    public static void Set(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe)
            k.SetValue(Name, $"\"{exe}\" --tray");
        else
            k.DeleteValue(Name, false);
    }
}
