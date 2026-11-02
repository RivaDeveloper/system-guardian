using Microsoft.Win32;

namespace Guardian.Services.Tools;

public sealed class InstalledProgram
{
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public long SizeKb { get; set; }
    public string UninstallCommand { get; set; } = "";
    public string Subtitle => string.Join(" · ", new[] { Publisher, Version, SizeKb > 0 ? Cleanup.JunkCategory.Format(SizeKb * 1024) : "" }.Where(s => s.Length > 0));
}

public static class InstalledPrograms
{
    public static List<InstalledProgram> List()
    {
        var found = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
        void Read(RegistryHive hive, string path)
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(path);
            if (root == null) return;
            foreach (var sub in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(sub);
                if (k == null) continue;
                if (Convert.ToInt32(k.GetValue("SystemComponent") ?? 0) == 1) continue;
                if (k.GetValue("ParentKeyName") != null) continue;
                var name = k.GetValue("DisplayName") as string;
                var cmd = k.GetValue("UninstallString") as string;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(cmd)) continue;
                found.TryAdd(name + "|" + (k.GetValue("DisplayVersion") as string), new InstalledProgram
                {
                    Name = name,
                    Publisher = k.GetValue("Publisher") as string ?? "",
                    Version = k.GetValue("DisplayVersion") as string ?? "",
                    SizeKb = Convert.ToInt64(k.GetValue("EstimatedSize") ?? 0),
                    UninstallCommand = cmd,
                });
            }
        }
        const string u = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        Read(RegistryHive.LocalMachine, u);
        Read(RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
        Read(RegistryHive.CurrentUser, u);
        return found.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
