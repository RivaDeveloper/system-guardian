using System.Diagnostics;
using Microsoft.Win32;

namespace Guardian.Services.Tools;

public sealed class PerfTweak
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Impact { get; set; } = "";
    public bool Recommended { get; set; }
    public bool NeedsAdmin { get; set; }
    public bool NeedsRestart { get; set; }
    public bool IsApplied { get; set; }
    /// <summary>True for settings that lower the PC's protection in exchange for speed. They are never part of "Apply recommended".</summary>
    public bool Advanced { get; set; }
    public string Risk { get; set; } = "";
    public string Subtitle =>
        $"{Impact} · {Description}" + (NeedsAdmin ? " · needs administrator" : "") + (NeedsRestart ? " · takes effect after restart or sign-out" : "");
}

/// <summary>
/// A short list of performance settings that are well understood, reversible and low risk. Every one is a plain Windows
/// setting that can be switched back here. Guardian deliberately does NOT turn off Memory Integrity, security features,
/// Windows services or Defender, and doesn't run "RAM optimizers": the small gain isn't worth the risk or doesn't exist.
/// </summary>
public static class PerformanceTweaks
{
    const string HighPerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";

    public static List<PerfTweak> List()
    {
        var laptop = HasBattery();
        var list = new List<PerfTweak>
        {
            new() { Key = "power", Title = "High performance power plan", Impact = "Biggest everyday gain",
                Description = laptop ? "stops the CPU slowing down to save power; drains a laptop battery faster" : "keeps the CPU ready instead of ramping down",
                Recommended = !laptop },
            new() { Key = "gamemode", Title = "Windows Game Mode", Impact = "Helps games",
                Description = "gives a running game priority and stops Windows Update interrupting it", Recommended = true },
            new() { Key = "gamedvr", Title = "Turn off background game recording", Impact = "Small gain in games",
                Description = "stops Xbox Game Bar capturing video in the background", Recommended = true },
            new() { Key = "startupdelay", Title = "Remove the startup delay", Impact = "Faster sign-in",
                Description = "Windows waits a few seconds before starting your startup programs; this removes the wait", Recommended = true },
            new() { Key = "deliveryopt", Title = "Don't upload updates to other PCs", Impact = "Saves bandwidth and background work",
                Description = "turns off Delivery Optimization sharing", NeedsAdmin = true, Recommended = true },
            new() { Key = "transparency", Title = "Turn off transparency effects", Impact = "Small gain on weak graphics",
                Description = "makes windows and the taskbar solid" },
            new() { Key = "mouse", Title = "Turn off mouse acceleration", Impact = "More precise aiming",
                Description = "pointer moves the same distance at any speed; not a speed boost, a precision one", NeedsRestart = true },
        };
        list.Add(new PerfTweak { Key = "hvci", Advanced = true, NeedsAdmin = true, NeedsRestart = true, Title = "Turn off Memory Integrity",
            Impact = "A few percent more frames in some games",
            Description = "Memory Integrity stops malicious or buggy drivers from running in the kernel",
            Risk = "Without Memory Integrity, a malicious driver can take over Windows more easily, and some anti-cheat and security software expects it on. Turn it back on here at any time." });
        list.Add(new PerfTweak { Key = "vbs", Advanced = true, NeedsAdmin = true, NeedsRestart = true, Title = "Turn off Virtualization-based security",
            Impact = "Removes the virtualization overhead on games and benchmarks",
            Description = "VBS runs parts of Windows security in an isolated space",
            Risk = "VBS protects credentials and kernel memory from attackers who already have some access. Without it that protection is gone, and Memory Integrity stops working too." });
        list.Add(new PerfTweak { Key = "mitigations", Advanced = true, NeedsAdmin = true, NeedsRestart = true, Title = "Turn off CPU vulnerability mitigations",
            Impact = "Noticeable only on older CPUs, little on recent ones",
            Description = "Windows' protection against Spectre and Meltdown style attacks",
            Risk = "These attacks let a malicious program or web page read data it shouldn't, such as passwords. Don't turn this off on a PC you browse or work on." });
        if (HasHags())
            list.Insert(2, new PerfTweak { Key = "hags", Title = "Hardware-accelerated GPU scheduling", Impact = "Can lower latency in games",
                Description = "lets the graphics card manage its own memory queue; a few games prefer it off", NeedsAdmin = true, NeedsRestart = true, Recommended = true });
        foreach (var t in list) t.IsApplied = Read(t.Key);
        return list;
    }

    // ---- reading and writing ------------------------------------------------------------------------

    static bool HasBattery()
    {
        try
        {
            var r = Run("powershell.exe", "-NoProfile -Command \"(Get-CimInstance Win32_Battery | Measure-Object).Count\"");
            return int.TryParse(r.Trim(), out var n) && n > 0;
        }
        catch { return false; }
    }

    const string GraphicsDrivers = @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers";
    const string DeviceGuard = @"SYSTEM\CurrentControlSet\Control\DeviceGuard";
    const string HvciPath = DeviceGuard + @"\Scenarios\HypervisorEnforcedCodeIntegrity";
    const string MemoryManagement = @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";

    static bool HasHags()
    {
        using var k = Registry.LocalMachine.OpenSubKey(GraphicsDrivers);
        return k?.GetValue("HwSchMode") != null;
    }

    static string Run(string exe, string args)
    {
        var psi = new ProcessStartInfo(exe, args) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit(15000);
        return o;
    }

    static string ActiveScheme()
    {
        var o = Run("powercfg.exe", "/getactivescheme");
        var m = System.Text.RegularExpressions.Regex.Match(o, @"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
        return m.Success ? m.Value.ToLowerInvariant() : "";
    }

    static int Dword(RegistryKey root, string path, string name, int fallback)
    {
        using var k = root.OpenSubKey(path);
        return k?.GetValue(name) is int i ? i : fallback;
    }

    static bool Read(string key) => key switch
    {
        "power" => ActiveScheme() is HighPerf or Ultimate,
        "gamemode" => Dword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1) != 0,
        "gamedvr" => Dword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1) == 0,
        "hags" => Dword(Registry.LocalMachine, GraphicsDrivers, "HwSchMode", 1) == 2,
        "startupdelay" => Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", -1) == 0,
        "deliveryopt" => Dword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config", "DODownloadMode", -1) == 0,
        "transparency" => Dword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 1) == 0,
        "mouse" => (Registry.CurrentUser.OpenSubKey(@"Control Panel\Mouse")?.GetValue("MouseSpeed") as string) == "0",
        "hvci" => Dword(Registry.LocalMachine, HvciPath, "Enabled", -1) == 0,
        "vbs" => Dword(Registry.LocalMachine, DeviceGuard, "EnableVirtualizationBasedSecurity", -1) == 0,
        "mitigations" => Dword(Registry.LocalMachine, MemoryManagement, "FeatureSettingsOverride", -1) == 3
                         && Dword(Registry.LocalMachine, MemoryManagement, "FeatureSettingsOverrideMask", -1) == 3,
        _ => false,
    };

    static void SetDword(RegistryKey root, string path, string name, int value)
    {
        using var k = root.CreateSubKey(path, true);
        k.SetValue(name, value, RegistryValueKind.DWord);
    }

    static void DeleteValue(RegistryKey root, string path, string name)
    {
        using var k = root.OpenSubKey(path, true);
        k?.DeleteValue(name, false);
    }

    public static (bool Ok, string Message) Set(string key, bool on)
    {
        try
        {
            switch (key)
            {
                case "power":
                    if (on)
                    {
                        // Prefer the built-in High performance plan; some PCs only have Ultimate Performance after duplicating it.
                        Run("powercfg.exe", $"/setactive {HighPerf}");
                        if (ActiveScheme() != HighPerf)
                        {
                            var dup = Run("powercfg.exe", $"/duplicatescheme {Ultimate}");
                            var m = System.Text.RegularExpressions.Regex.Match(dup, @"[0-9a-fA-F]{8}(-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
                            if (m.Success) Run("powercfg.exe", $"/setactive {m.Value}");
                        }
                        return ActiveScheme() is HighPerf || Read("power") ? (true, "") : (false, "This PC doesn't allow switching power plans (it may use modern standby).");
                    }
                    Run("powercfg.exe", $"/setactive {Balanced}");
                    return (true, "");
                case "gamemode":
                    SetDword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", on ? 1 : 0);
                    SetDword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AllowAutoGameMode", on ? 1 : 0);
                    break;
                case "gamedvr":
                    SetDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", on ? 0 : 1);
                    SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", on ? 0 : 1);
                    break;
                case "hags":
                    SetDword(Registry.LocalMachine, GraphicsDrivers, "HwSchMode", on ? 2 : 1);
                    break;
                case "startupdelay":
                    if (on) SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", 0);
                    else DeleteValue(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec");
                    break;
                case "deliveryopt":
                    if (on) SetDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config", "DODownloadMode", 0);
                    else DeleteValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config", "DODownloadMode");
                    break;
                case "transparency":
                    SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", on ? 0 : 1);
                    break;
                case "hvci":
                    SetDword(Registry.LocalMachine, HvciPath, "Enabled", on ? 0 : 1);
                    break;
                case "vbs":
                    SetDword(Registry.LocalMachine, DeviceGuard, "EnableVirtualizationBasedSecurity", on ? 0 : 1);
                    break;
                case "mitigations":
                    // Switching back means removing the override entirely, which restores Windows' default protection.
                    if (on)
                    {
                        SetDword(Registry.LocalMachine, MemoryManagement, "FeatureSettingsOverride", 3);
                        SetDword(Registry.LocalMachine, MemoryManagement, "FeatureSettingsOverrideMask", 3);
                    }
                    else
                    {
                        DeleteValue(Registry.LocalMachine, MemoryManagement, "FeatureSettingsOverride");
                        DeleteValue(Registry.LocalMachine, MemoryManagement, "FeatureSettingsOverrideMask");
                    }
                    break;
                case "mouse":
                    using (var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Mouse", true))
                    {
                        k.SetValue("MouseSpeed", on ? "0" : "1", RegistryValueKind.String);
                        k.SetValue("MouseThreshold1", on ? "0" : "6", RegistryValueKind.String);
                        k.SetValue("MouseThreshold2", on ? "0" : "10", RegistryValueKind.String);
                    }
                    break;
            }
            return (true, "");
        }
        catch (UnauthorizedAccessException) { return (false, "Windows refused: restart Guardian as administrator."); }
        catch (Exception ex) { return (false, ex.Message); }
    }
}
