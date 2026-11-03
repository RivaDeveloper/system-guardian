using System.Text.RegularExpressions;

namespace Guardian.Services.Tools;

public enum DriverState { Newer, UpToDate, Unknown, NotFound, Detected }

/// <summary>One driver or software package the PC uses, found by looking at what is installed.</summary>
public sealed class DetectedDriver
{
    public string Category { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Date { get; set; } = "";
    public string Vendor { get; set; } = "";
    /// <summary>False when the installed number uses a different scheme from the maker's download pages.</summary>
    public bool Comparable { get; set; } = true;
}

/// <summary>One row of a manufacturer's download table, read from text the user pasted.</summary>
public sealed class VendorEntry
{
    public string Section { get; set; } = "";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "";
    public DateTime? Date { get; set; }
}

public sealed partial class DriverCheck
{
    public string Category { get; set; } = "";
    public string Installed { get; set; } = "";
    public string Latest { get; set; } = "";
    public string Title { get; set; } = "";
    public DriverState State { get; set; }
    public string Note { get; set; } = "";
    public string StatusText => State switch
    {
        DriverState.Newer => "Newer on the site",
        DriverState.UpToDate => "Up to date",
        DriverState.NotFound => "Not on the page",
        DriverState.Detected => "Detected",
        _ => "Check by eye",
    };
    public string Subtitle => Installed.Length == 0 ? Note : $"Installed {Installed}" + (Latest.Length > 0 ? $"  ·  Site {Latest}" : "") + (Note.Length > 0 ? $"  ·  {Note}" : "");
}

/// <summary>
/// Detects the drivers that PC makers list on their download pages (chipset, audio, LAN, Wi-Fi, Bluetooth, graphics)
/// and compares them with a download table the user copies from the maker's site. Manufacturer sites block automated
/// requests, so the table comes from the user's own browser rather than from Guardian.
/// </summary>
public static class ManufacturerDrivers
{
    static readonly string[] Categories = { "Chipset", "Audio", "LAN", "Wi-Fi", "Bluetooth", "Graphics" };

    public static async Task<List<DetectedDriver>> DetectAsync()
    {
        var r = await ProcessRunner.RunPowerShellAsync(
            "Get-CimInstance Win32_PnPSignedDriver | Where-Object { $_.DeviceName -and $_.DriverVersion } | ForEach-Object { " +
            "$d = ''; if ($_.DriverDate) { $d = $_.DriverDate.ToString('yyyy-MM-dd') }; " +
            "\"$($_.DeviceClass)|$($_.DeviceName)|$($_.Manufacturer)|$($_.DriverVersion)|$d\" }");
        var pnp = new List<(string Class, string Name, string Maker, string Version, string Date)>();
        foreach (var line in r.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var p = line.Split('|');
            if (p.Length >= 5) pnp.Add((p[0].ToUpperInvariant(), p[1], p[2], p[3], p[4]));
        }
        var cr = await ProcessRunner.RunPowerShellAsync("Get-CimInstance Win32_Processor | Select-Object -First 1 | ForEach-Object { $_.Name.Trim() + '|' + $_.Manufacturer + '|' + $_.NumberOfCores + '|' + $_.NumberOfLogicalProcessors }");
        var cp = cr.Output.Trim().Split('|');
        (string, string, string, string)? cpu = cp.Length >= 4 ? (cp[0], cp[1], cp[2], cp[3]) : null;
        return Detect(pnp, InstalledPrograms.List(), cpu);
    }

    internal static List<DetectedDriver> Detect(List<(string Class, string Name, string Maker, string Version, string Date)> pnp, List<InstalledProgram> programs, (string Name, string Maker, string Cores, string Threads)? cpu = null)
    {
        var found = new List<DetectedDriver>();
        void Add(string cat, string name, string version, string date) =>
            found.Add(new DetectedDriver { Category = cat, Name = name, Version = version, Date = date });

        // Chipset: the vendor package shows up as an installed program, not as one driver.
        var chip = programs.FirstOrDefault(p => Regex.IsMatch(p.Name, @"(AMD|Intel).*Chipset", RegexOptions.IgnoreCase));
        if (chip != null) Add("Chipset", chip.Name, chip.Version, "");

        var audio = pnp.Where(d => d.Class == "MEDIA" && Regex.IsMatch(d.Name, "Audio", RegexOptions.IgnoreCase)
                                && !Regex.IsMatch(d.Name, @"NVIDIA|AMD High Definition|Intel.*Display|Virtual|USB|Bluetooth|Steam|Oculus|Remote", RegexOptions.IgnoreCase))
                       .OrderByDescending(d => d.Maker.Contains("Realtek", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        if (audio.Name != null) Add("Audio", audio.Name, audio.Version, audio.Date);

        var net = pnp.Where(d => d.Class == "NET" && !Regex.IsMatch(d.Name, @"Virtual|VPN|TAP|Miniport|WAN|Hyper-V|VMware|VirtualBox|Bluetooth|Loopback|Npcap|Tunnel", RegexOptions.IgnoreCase)).ToList();
        var lan = net.FirstOrDefault(d => Regex.IsMatch(d.Name, @"Ethernet|GbE|Gigabit|2\.5G|PCIe.*Controller", RegexOptions.IgnoreCase)
                                      && !Regex.IsMatch(d.Name, @"Wi-?Fi|Wireless|WLAN|802\.11", RegexOptions.IgnoreCase));
        if (lan.Name != null) Add("LAN", lan.Name, lan.Version, lan.Date);
        var wifi = net.FirstOrDefault(d => Regex.IsMatch(d.Name, @"Wi-?Fi|Wireless|WLAN|802\.11", RegexOptions.IgnoreCase));
        if (wifi.Name != null) Add("Wi-Fi", wifi.Name, wifi.Version, wifi.Date);

        var bt = pnp.Where(d => d.Class == "BLUETOOTH" && Regex.IsMatch(d.Name, @"Bluetooth|Wireless", RegexOptions.IgnoreCase)
                             && !Regex.IsMatch(d.Name, @"Enumerator|Device$|LE Generic|Microsoft", RegexOptions.IgnoreCase))
                    .OrderByDescending(d => !d.Maker.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)).FirstOrDefault();
        if (bt.Name != null) Add("Bluetooth", bt.Name, bt.Version, bt.Date);

        // Graphics: every display adapter, shown with the version number the maker's download page uses.
        var amdSoftware = programs.FirstOrDefault(p => Regex.IsMatch(p.Name, @"^AMD Software", RegexOptions.IgnoreCase));
        var gpus = pnp.Where(d => d.Class == "DISPLAY" && !Regex.IsMatch(d.Name, @"Basic|Remote|Virtual|Parsec|Indirect", RegexOptions.IgnoreCase))
                      .GroupBy(d => d.Name).Select(g => g.First()).ToList();
        var n = 0;
        foreach (var g in gpus)
        {
            var cat = n++ == 0 ? "Graphics" : $"Graphics {n}";
            var vendor = GpuVendor(g.Name, g.Maker);
            var version = g.Version; var comparable = false;
            if (vendor == "NVIDIA") { version = NvidiaVersion(g.Version); comparable = true; }
            else if (vendor == "Intel") { version = IntelVersion(g.Version); comparable = true; }
            else if (vendor == "AMD" && amdSoftware != null) { version = amdSoftware.Version; comparable = true; }
            found.Add(new DetectedDriver { Category = cat, Name = g.Name, Version = version, Date = g.Date, Vendor = vendor, Comparable = comparable });
        }

        if (cpu is { } c)
        {
            var micro = Microcode();
            found.Add(new DetectedDriver
            {
                Category = "Processor", Name = $"{c.Name} ({c.Cores} cores, {c.Threads} threads)", Version = micro, Date = "",
                Vendor = Regex.IsMatch(c.Maker + c.Name, "AMD", RegexOptions.IgnoreCase) ? "AMD" : Regex.IsMatch(c.Maker + c.Name, "Intel", RegexOptions.IgnoreCase) ? "Intel" : "",
                Comparable = false,
            });
        }
        return found;
    }

    static string GpuVendor(string name, string maker) =>
        Regex.IsMatch(name + maker, "NVIDIA|GeForce", RegexOptions.IgnoreCase) ? "NVIDIA" :
        Regex.IsMatch(name + maker, "AMD|Radeon|Advanced Micro", RegexOptions.IgnoreCase) ? "AMD" :
        Regex.IsMatch(name + maker, "Intel", RegexOptions.IgnoreCase) ? "Intel" : "";

    /// <summary>Windows reports NVIDIA drivers as 32.0.15.6094; the number on NVIDIA's site is 560.94.</summary>
    internal static string NvidiaVersion(string windows)
    {
        var p = windows.Split('.');
        if (p.Length != 4 || p[2].Length == 0) return windows;
        var s = p[2][^1] + p[3].PadLeft(4, '0');
        return s[..^2] + "." + s[^2..];
    }

    /// <summary>Windows reports Intel graphics as 32.0.101.6881; Intel's site shows 101.6881.</summary>
    internal static string IntelVersion(string windows)
    {
        var p = windows.Split('.');
        return p.Length == 4 ? p[2] + "." + p[3] : windows;
    }

    static string Microcode()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (k?.GetValue("Update Revision") is byte[] { Length: 8 } b && BitConverter.ToUInt32(b, 4) != 0)
                return "microcode 0x" + BitConverter.ToUInt32(b, 4).ToString("X");
        }
        catch { }
        return "";
    }

    /// <summary>The official driver pages for this PC's graphics card and processor.</summary>
    public static List<string> VendorPages(List<DetectedDriver> detected)
    {
        var urls = new List<string>();
        foreach (var d in detected.Where(d => d.Category.StartsWith("Graphics") || d.Category == "Processor"))
        {
            var url = (d.Category, d.Vendor) switch
            {
                (_, "NVIDIA") => "https://www.nvidia.com/en-us/drivers/",
                ("Processor", "AMD") => "https://www.amd.com/en/support/download/drivers.html",
                ("Processor", "Intel") => "https://www.intel.com/content/www/us/en/support/detect.html",
                (_, "AMD") => "https://www.amd.com/en/support/download/drivers.html",
                (_, "Intel") => "https://www.intel.com/content/www/us/en/support/detect.html",
                _ => null,
            };
            if (url != null && !urls.Contains(url)) urls.Add(url);
        }
        return urls;
    }

    // ---- reading a pasted download table ---------------------------------------------------------

    static readonly Regex VersionRx = new(@"^\d+(\.\d+){1,4}$", RegexOptions.Compiled);
    static readonly Regex DateRx = new(@"^[A-Z][a-z]{2} \d{1,2}, \d{4}$|^\d{4}[-/]\d{2}[-/]\d{2}$", RegexOptions.Compiled);
    static readonly Regex SectionRx = new(@"^(Audio|Chipset|LAN|Wireless LAN|WLAN|Wi-?Fi|Bluetooth|VGA|Graphics|Storage|Touch ?pad|Utility|Others?|USB|Thunderbolt|Card Reader|SATA|RAID)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Reads text copied from a download page: section titles, then rows of description, version, OS, size and date.</summary>
    public static List<VendorEntry> ParsePaste(string text)
    {
        var tokens = text.Replace("\r", "").Split(new[] { '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim()).Where(t => t.Length > 0).ToList();
        var entries = new List<VendorEntry>();
        var section = "";
        for (var i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i];
            if (SectionRx.IsMatch(t)) { section = t; continue; }
            if (!VersionRx.IsMatch(t) || i == 0) continue;
            var desc = tokens[i - 1];
            if (VersionRx.IsMatch(desc) || DateRx.IsMatch(desc) || SectionRx.IsMatch(desc) || desc.Equals("Version", StringComparison.OrdinalIgnoreCase)) continue;
            DateTime? date = null;
            for (var j = i + 1; j < Math.Min(tokens.Count, i + 8); j++)
            {
                if (VersionRx.IsMatch(tokens[j])) break;
                if (DateRx.IsMatch(tokens[j]) && DateTime.TryParse(tokens[j], System.Globalization.CultureInfo.InvariantCulture, out var d)) { date = d; break; }
            }
            entries.Add(new VendorEntry { Section = section, Description = desc, Version = t, Date = date });
        }
        return entries;
    }

    // ---- comparing --------------------------------------------------------------------------------

    static bool Matches(string category, VendorEntry e)
    {
        var d = e.Description; var s = e.Section;
        bool Has(string rx) => Regex.IsMatch(d, rx, RegexOptions.IgnoreCase);
        bool Sec(string rx) => Regex.IsMatch(s, rx, RegexOptions.IgnoreCase);
        return category switch
        {
            "Chipset" => (Sec("^Chipset$") || Has("Chipset")) && !Has("APU|VGA|Graphics"),
            "Audio" => Sec("^Audio$") || Has("Audio"),
            "LAN" => (Sec("^LAN$") || Has(@"\bLAN\b|Ethernet")) && !Has("Wireless|WLAN|Wi-?Fi"),
            "Wi-Fi" => Sec("Wireless|WLAN|Wi-?Fi") || Has("Wireless|WLAN|Wi-?Fi"),
            "Bluetooth" => Sec("Bluetooth") || Has("Bluetooth"),
            "Graphics" => Sec("VGA|Graphics") || Has("APU|VGA|Graphics|Radeon|GeForce"),
            _ => false,
        };
    }

    /// <summary>Positive when <paramref name="latest"/> is newer, negative when older, 0 when equal.</summary>
    public static int CompareDotted(string installed, string latest)
    {
        static int[] Parts(string v) => Regex.Matches(v, @"\d+").Select(m => int.TryParse(m.Value, out var n) ? n : 0).ToArray();
        var a = Parts(installed); var b = Parts(latest);
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0; var y = i < b.Length ? b[i] : 0;
            if (x != y) return y.CompareTo(x);
        }
        return 0;
    }

    public static List<DriverCheck> Compare(List<DetectedDriver> detected, List<VendorEntry> entries)
    {
        var result = new List<DriverCheck>();
        foreach (var cat in Categories)
        {
            var mine = detected.FirstOrDefault(d => d.Category == cat);
            var candidates = entries.Where(e => Matches(cat, e)).ToList();
            // The newest listed entry for this kind of driver.
            var newest = candidates.OrderByDescending(e => e.Date ?? DateTime.MinValue).ThenByDescending(e => e.Version, Comparer<string>.Create(CompareDotted2)).FirstOrDefault();
            if (mine == null && newest == null) continue;

            var check = new DriverCheck { Category = cat, Title = mine?.Name ?? $"{cat} (not detected on this PC)", Installed = mine?.Version ?? "" };
            if (newest == null) { check.State = DriverState.NotFound; check.Note = "no matching row in the pasted table"; }
            else if (mine == null) { check.State = DriverState.Unknown; check.Latest = newest.Version; check.Note = $"{newest.Description}; not detected here"; }
            else
            {
                check.Latest = newest.Version;
                // Windows driver versions often use a different scheme from the maker's package number (graphics especially).
                var comparable = mine.Comparable;
                // A first number with a different number of digits (1125.x against 11.x) means two different schemes.
                var firstA = Regex.Match(mine.Version, @"\d+").Value; var firstB = Regex.Match(newest.Version, @"\d+").Value;
                if (firstA.Length != firstB.Length) comparable = false;
                var c = CompareDotted(mine.Version, newest.Version);
                if (!comparable) { check.State = DriverState.Unknown; check.Note = $"{newest.Description}; version numbers use different schemes" + (newest.Date is { } dt ? $", site dated {dt:yyyy-MM-dd}" : ""); }
                else if (c > 0) { check.State = DriverState.Newer; check.Note = newest.Description + (newest.Date is { } d2 ? $", {d2:yyyy-MM-dd}" : ""); }
                else { check.State = DriverState.UpToDate; check.Note = c < 0 ? "yours is newer than the site's listing" : newest.Description; }
            }
            result.Add(check);
        }
        // Extra graphics cards and the processor have no row on a board's page: they are listed with a pointer to their maker.
        foreach (var d in detected.Where(d => d.Category == "Processor" || (d.Category.StartsWith("Graphics ") && d.Category != "Graphics")))
            result.Add(new DriverCheck
            {
                Category = d.Category == "Processor" ? "Processor" : d.Category, Title = d.Name, Installed = d.Version, State = DriverState.Detected,
                Note = d.Category == "Processor" ? "CPU fixes arrive through BIOS updates and the chipset driver above" : "check this card on its maker's page (use the buttons above)",
            });
        return result;
    }

    static int CompareDotted2(string? a, string? b) => CompareDotted(b ?? "", a ?? "");
}
