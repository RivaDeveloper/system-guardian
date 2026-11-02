using Microsoft.Win32;

namespace Guardian.Services.Tools;

public sealed class StartupItem
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public string Source { get; set; } = "";
    public bool IsEnabled { get; set; }
    public bool Machine { get; set; }
    /// <summary>The StartupApproved subkey this entry is tracked in ("Run", "Run32" or "StartupFolder").</summary>
    public string ApprovedKey { get; set; } = "Run";
    public string ApprovedName { get; set; } = "";
    public string Subtitle => $"{Source} · {Command}";
}

/// <summary>
/// Lists startup programs and switches them on or off the same way Task Manager does:
/// through the StartupApproved registry keys. Nothing is deleted.
/// </summary>
public static class StartupService
{
    const string RunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string Run32Path = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    const string ApprovedPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    static RegistryKey Base(bool machine) =>
        RegistryKey.OpenBaseKey(machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);

    public static List<StartupItem> List()
    {
        var list = new List<StartupItem>();
        foreach (var (machine, path, approved) in new[] { (false, RunPath, "Run"), (true, RunPath, "Run"), (true, Run32Path, "Run32") })
        {
            using var k = Base(machine).OpenSubKey(path);
            if (k == null) continue;
            foreach (var name in k.GetValueNames())
            {
                var cmd = k.GetValue(name) as string ?? "";
                list.Add(new StartupItem
                {
                    Name = name, Command = cmd, Machine = machine, ApprovedKey = approved, ApprovedName = name,
                    Source = machine ? "All users" : "This user", IsEnabled = IsApproved(machine, approved, name),
                });
            }
        }
        foreach (var (machine, dir) in new[]
        {
            (false, Environment.GetFolderPath(Environment.SpecialFolder.Startup)),
            (true, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)),
        })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir))
            {
                var file = Path.GetFileName(f);
                if (file.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new StartupItem
                {
                    Name = Path.GetFileNameWithoutExtension(f), Command = f, Machine = machine, ApprovedKey = "StartupFolder",
                    ApprovedName = file, Source = machine ? "Startup folder (all users)" : "Startup folder", IsEnabled = IsApproved(machine, "StartupFolder", file),
                });
            }
        }
        return list.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static bool IsApproved(bool machine, string key, string name)
    {
        try
        {
            using var k = Base(machine).OpenSubKey(ApprovedPath + key);
            return k?.GetValue(name) is not byte[] b || b.Length == 0 || (b[0] & 1) == 0;
        }
        catch { return true; }
    }

    public static void SetEnabled(StartupItem item, bool enabled)
    {
        using var k = Base(item.Machine).CreateSubKey(ApprovedPath + item.ApprovedKey, true);
        var data = new byte[12];
        data[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        k.SetValue(item.ApprovedName, data, RegistryValueKind.Binary);
    }
}
