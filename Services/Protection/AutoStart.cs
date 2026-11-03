using Microsoft.Win32;

namespace Guardian.Services.Protection;

/// <summary>Starts Guardian quietly in the tray when you sign in to Windows (a normal per-user Run entry, no service or driver).</summary>
public static class AutoStart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Guardian";

    public static bool IsEnabled
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey);
            return k?.GetValue(ValueName) is string s && s.Contains("--background");
        }
    }

    public static void Set(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
        if (on) k.SetValue(ValueName, $"\"{Environment.ProcessPath}\" --background");
        else k.DeleteValue(ValueName, false);
    }
}
