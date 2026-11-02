using System.Security.Cryptography;
using Microsoft.Win32;

namespace Guardian.Services.Tools;

public sealed class BiosInfo
{
    public string BiosVendor { get; set; } = "";
    public string BiosVersion { get; set; } = "";
    public string BiosDate { get; set; } = "";
    public string BoardVendor { get; set; } = "";
    public string BoardModel { get; set; } = "";
    public string SystemVendor { get; set; } = "";
    public string SystemModel { get; set; } = "";
    public bool Uefi { get; set; }
    public bool SecureBoot { get; set; }
    public bool HasBattery { get; set; }
    public bool OnAc { get; set; }
    public int BatteryPercent { get; set; }

    /// <summary>The name to look the firmware up under: laptops and prebuilts use the system model, custom PCs the board.</summary>
    public string SearchVendor => Generic(SystemVendor) ? BoardVendor : SystemVendor;
    public string SearchModel => Generic(SystemModel) ? BoardModel : SystemModel;

    static bool Generic(string s) =>
        string.IsNullOrWhiteSpace(s) || s.Contains("To Be Filled", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("System Product", StringComparison.OrdinalIgnoreCase) || s.Contains("System manufacturer", StringComparison.OrdinalIgnoreCase) ||
        s.Equals("Default string", StringComparison.OrdinalIgnoreCase) || s.Contains("O.E.M", StringComparison.OrdinalIgnoreCase);
}

public sealed record FileCheck(string Sha256, long Size, string Signer, string SignatureNote);

/// <summary>
/// Everything around a BIOS update except the flash itself. Guardian never writes firmware: a failed flash can leave a PC
/// unable to start, so the flash is always done by the manufacturer's own tool. This class reads the facts, runs the
/// protective steps (restore point, BitLocker suspend) and checks a downloaded file.
/// </summary>
public static class BiosService
{
    const string InfoScript = """
        $b = Get-CimInstance Win32_BIOS
        $bb = Get-CimInstance Win32_BaseBoard
        $cs = Get-CimInstance Win32_ComputerSystem
        $bat = Get-CimInstance Win32_Battery | Select-Object -First 1
        "BiosVendor=$($b.Manufacturer)"
        "BiosVersion=$($b.SMBIOSBIOSVersion)"
        "BiosDate=$($b.ReleaseDate.ToString('yyyy-MM-dd'))"
        "BoardVendor=$($bb.Manufacturer)"
        "BoardModel=$($bb.Product)"
        "SystemVendor=$($cs.Manufacturer)"
        "SystemModel=$($cs.Model)"
        if ($bat) { "Battery=$($bat.EstimatedChargeRemaining)"; "Ac=$($bat.BatteryStatus -in 2,6,7,8,9)" }
        """;

    public static async Task<BiosInfo> ReadAsync()
    {
        var r = await ProcessRunner.RunPowerShellAsync(InfoScript);
        var map = new Dictionary<string, string>();
        foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var i = line.IndexOf('=');
            if (i > 0) map[line[..i]] = line[(i + 1)..];
        }
        string Get(string k) => map.TryGetValue(k, out var v) ? v : "";
        var info = new BiosInfo
        {
            BiosVendor = Get("BiosVendor"), BiosVersion = Get("BiosVersion"), BiosDate = Get("BiosDate"),
            BoardVendor = Get("BoardVendor"), BoardModel = Get("BoardModel"),
            SystemVendor = Get("SystemVendor"), SystemModel = Get("SystemModel"),
            HasBattery = map.ContainsKey("Battery"),
        };
        if (info.HasBattery)
        {
            int.TryParse(Get("Battery"), out var pct);
            info.BatteryPercent = pct;
            info.OnAc = Get("Ac").Equals("True", StringComparison.OrdinalIgnoreCase);
        }
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            info.Uefi = k != null;
            info.SecureBoot = Convert.ToInt32(k?.GetValue("UEFISecureBootEnabled") ?? 0) == 1;
        }
        catch { }
        return info;
    }

    /// <summary>"On", "Off", or null when it can't be read (needs administrator).</summary>
    public static async Task<string?> BitLockerAsync()
    {
        if (!Elevation.IsElevated) return null;
        var r = await ProcessRunner.RunPowerShellAsync("try { (Get-BitLockerVolume -MountPoint $env:SystemDrive -ErrorAction Stop).ProtectionStatus.ToString() } catch { 'Unknown' }");
        var s = r.Output.Trim();
        return s is "On" or "Off" ? s : null;
    }

    public static async Task<(bool Ok, string Message)> CreateRestorePointAsync()
    {
        var r = await ProcessRunner.RunPowerShellAsync(
            "try { Checkpoint-Computer -Description 'Guardian: before BIOS update' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'OK' } catch { $_.Exception.Message }");
        var o = r.Output.Trim();
        return o == "OK" ? (true, "Restore point created.") : (false, o.Length > 0 ? o : "Windows couldn't create a restore point (System Protection may be off).");
    }

    /// <summary>Pauses BitLocker for exactly one restart so a firmware change doesn't trigger a recovery-key prompt.</summary>
    public static async Task<(bool Ok, string Message)> SuspendBitLockerOnceAsync()
    {
        var r = await ProcessRunner.RunPowerShellAsync(
            "try { Suspend-BitLocker -MountPoint $env:SystemDrive -RebootCount 1 -ErrorAction Stop | Out-Null; 'OK' } catch { $_.Exception.Message }");
        var o = r.Output.Trim();
        return o == "OK" ? (true, "BitLocker is paused for one restart and turns itself back on afterwards.") : (false, o);
    }

    static readonly Dictionary<string, string> Domains = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gigabyte"] = "gigabyte.com", ["asus"] = "asus.com", ["micro-star"] = "msi.com", ["msi"] = "msi.com",
        ["asrock"] = "asrock.com", ["dell"] = "dell.com", ["hp"] = "hp.com", ["hewlett"] = "hp.com", ["lenovo"] = "lenovo.com",
        ["acer"] = "acer.com", ["microsoft"] = "microsoft.com", ["samsung"] = "samsung.com", ["biostar"] = "biostar.com.tw",
        ["supermicro"] = "supermicro.com", ["intel"] = "intel.com", ["framework"] = "frame.work", ["razer"] = "razer.com",
    };

    /// <summary>A web search limited to the maker's official site, or null when the maker isn't recognised.</summary>
    public static string? SupportSearchUrl(BiosInfo info)
    {
        var vendor = info.SearchVendor;
        var domain = Domains.FirstOrDefault(d => vendor.Contains(d.Key, StringComparison.OrdinalIgnoreCase)).Value;
        if (domain == null || string.IsNullOrWhiteSpace(info.SearchModel)) return null;
        var q = Uri.EscapeDataString($"site:{domain} {info.SearchModel} BIOS download");
        return "https://duckduckgo.com/?q=" + q;
    }

    /// <summary>
    /// Compares versions like "F44b" and "F45": positive when <paramref name="latest"/> is newer, negative when older,
    /// 0 when equal, int.MinValue when the two don't follow the same pattern.
    /// </summary>
    public static int CompareVersions(string installed, string latest)
    {
        static System.Text.RegularExpressions.Match Parse(string v) =>
            System.Text.RegularExpressions.Regex.Match(v.Trim(), @"^([A-Za-z]*)\.?(\d+)([A-Za-z]*)$");
        var a = Parse(installed);
        var b = Parse(latest);
        if (!a.Success || !b.Success || !a.Groups[1].Value.Equals(b.Groups[1].Value, StringComparison.OrdinalIgnoreCase)) return int.MinValue;
        var n = int.Parse(b.Groups[2].Value).CompareTo(int.Parse(a.Groups[2].Value));
        if (n != 0) return n;
        return string.Compare(b.Groups[3].Value, a.Groups[3].Value, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<FileCheck> CheckFileAsync(string path)
    {
        string hash;
        await using (var s = File.OpenRead(path))
            hash = Convert.ToHexString(await SHA256.HashDataAsync(s));
        var sig = Deep.Authenticode.Check(path);
        var note = sig.Status switch
        {
            Deep.SignatureStatus.Valid => $"Signed by {sig.Signer}; the signature is intact.",
            Deep.SignatureStatus.Tampered => "The signature does NOT match the file. Don't use it: re-download from the manufacturer.",
            Deep.SignatureStatus.Untrusted => $"Signed by {sig.Signer}, but Windows doesn't trust that certificate.",
            Deep.SignatureStatus.Unsigned => "Not signed. Raw BIOS images usually aren't. Compare the SHA-256 below with the one on the manufacturer's page, if they publish one.",
            _ => "This file type isn't signed. Compare the SHA-256 below with the manufacturer's, if they publish one.",
        };
        return new FileCheck(hash, new FileInfo(path).Length, sig.Signer, note);
    }

    public static string BuildReport(BiosInfo i, string? bitlocker)
    {
        return $"""
            Guardian BIOS information, saved {DateTime.Now:yyyy-MM-dd HH:mm}

            BIOS vendor:   {i.BiosVendor}
            BIOS version:  {i.BiosVersion}
            BIOS date:     {i.BiosDate}
            Board:         {i.BoardVendor} {i.BoardModel}
            System:        {i.SystemVendor} {i.SystemModel}
            Firmware mode: {(i.Uefi ? "UEFI" : "Legacy BIOS")}, Secure Boot {(i.SecureBoot ? "on" : "off")}
            BitLocker (system drive): {bitlocker ?? "not checked"}

            Keep this with your notes. If an update goes wrong, the version above is what to restore.
            If BitLocker is on, make sure you can reach your recovery key before changing firmware:
            https://account.microsoft.com/devices/recoverykey
            """;
    }
}
