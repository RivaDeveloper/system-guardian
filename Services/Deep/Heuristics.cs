using System.Text.RegularExpressions;

namespace Guardian.Services.Deep;

public sealed record Location(string Label, int Risk);

/// <summary>Rules of thumb for telling ordinary files from ones that malware likes to be.</summary>
public static class Heuristics
{
    public static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    public static readonly string System32 = Path.Combine(WindowsDir, "System32");
    public static readonly string SysWow64 = Path.Combine(WindowsDir, "SysWOW64");
    static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    static readonly string ProgramFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    public static readonly string ProgramData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    public static readonly string UsersDir = Path.Combine(Path.GetPathRoot(WindowsDir) ?? "C:\\", "Users");

    public static bool Under(string path, string dir) =>
        dir.Length > 0 && path.StartsWith(dir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    public static bool IsInWindowsDir(string path) => Under(path, WindowsDir);

    /// <summary>Where a file lives, and how much that location alone raises suspicion for an unsigned program.</summary>
    public static Location Where(string path)
    {
        var p = path;
        if (Under(p, Path.Combine(WindowsDir, "Temp"))) return new("the Windows Temp folder", 30);
        if (p.Contains("\\$Recycle.Bin\\", StringComparison.OrdinalIgnoreCase)) return new("the Recycle Bin", 40);
        if (Under(p, Path.Combine(UsersDir, "Public"))) return new("the Public user folder", 32);
        if (p.Contains("\\AppData\\Local\\Temp\\", StringComparison.OrdinalIgnoreCase)) return new("a Temp folder", 30);
        if (p.Contains("\\Downloads\\", StringComparison.OrdinalIgnoreCase)) return new("a Downloads folder", 10);
        if (p.Contains("\\AppData\\Roaming\\", StringComparison.OrdinalIgnoreCase)) return new("AppData\\Roaming", 20);
        if (p.Contains("\\AppData\\Local\\Programs\\", StringComparison.OrdinalIgnoreCase)) return new("AppData\\Local\\Programs", 0);
        if (p.Contains("\\AppData\\Local\\", StringComparison.OrdinalIgnoreCase)) return new("AppData\\Local", 12);
        if (Under(p, ProgramData)) return new("ProgramData", 22);
        if (Regex.IsMatch(p, @"^[A-Za-z]:\\[^\\]+\.(exe|scr|bat|cmd|com|vbs|js)$", RegexOptions.IgnoreCase))
            return new("the root of a drive", 25);
        if (IsInWindowsDir(p)) return new("the Windows folder", 0);
        if (Under(p, ProgramFiles) || Under(p, ProgramFilesX86)) return new("Program Files", 0);
        return new("another folder", 5);
    }

    /// <summary>Folders where programs rarely have a reason to live. Used to decide how closely to look.</summary>
    public static bool IsHotZone(string path) =>
        Under(path, Path.Combine(WindowsDir, "Temp"))
        || path.Contains("\\$Recycle.Bin\\", StringComparison.OrdinalIgnoreCase)
        || Under(path, Path.Combine(UsersDir, "Public"))
        || path.Contains("\\AppData\\Local\\Temp\\", StringComparison.OrdinalIgnoreCase)
        || Under(path, ProgramData);

    /// <summary>The strictest hiding places: temp folders, the Recycle Bin and the Public folder. (ProgramData is excluded: vendors legitimately keep files there.)</summary>
    public static bool IsTightHotZone(string path) =>
        Under(path, Path.Combine(WindowsDir, "Temp"))
        || path.Contains("\\$Recycle.Bin\\", StringComparison.OrdinalIgnoreCase)
        || Under(path, Path.Combine(UsersDir, "Public"))
        || path.Contains("\\AppData\\Local\\Temp\\", StringComparison.OrdinalIgnoreCase);

    // ---- names ----

    static readonly Dictionary<string, string[]> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        ["svchost.exe"] = new[] { System32, SysWow64 }, ["csrss.exe"] = new[] { System32 }, ["lsass.exe"] = new[] { System32 },
        ["services.exe"] = new[] { System32 }, ["smss.exe"] = new[] { System32 }, ["wininit.exe"] = new[] { System32 },
        ["winlogon.exe"] = new[] { System32 }, ["taskhostw.exe"] = new[] { System32 }, ["spoolsv.exe"] = new[] { System32 },
        ["dwm.exe"] = new[] { System32 }, ["conhost.exe"] = new[] { System32 }, ["runtimebroker.exe"] = new[] { System32 },
        ["fontdrvhost.exe"] = new[] { System32 }, ["searchindexer.exe"] = new[] { System32 }, ["sihost.exe"] = new[] { System32 },
        ["dllhost.exe"] = new[] { System32, SysWow64 }, ["audiodg.exe"] = new[] { System32 }, ["wmiprvse.exe"] = new[] { System32, SysWow64 },
        ["ctfmon.exe"] = new[] { System32 }, ["explorer.exe"] = new[] { WindowsDir }, ["lsm.exe"] = new[] { System32 },
    };

    /// <summary>Returns a reason if a program uses a Windows process name from the wrong place, or one letter off.</summary>
    public static string? Masquerade(string path, bool includeNearMiss)
    {
        var name = Path.GetFileName(path);
        var dir = Path.GetDirectoryName(path) ?? "";
        if (SystemProcesses.TryGetValue(name, out var allowed))
            return allowed.Any(a => dir.Equals(a, StringComparison.OrdinalIgnoreCase))
                ? null
                : $"Named like the Windows process {name} but it is running from {dir}";

        // One-letter-off names are only suspicious outside ordinary install folders (a program called service.exe in Program Files is just a program).
        if (!includeNearMiss) return null;
        foreach (var real in SystemProcesses.Keys)
            if (Math.Abs(real.Length - name.Length) <= 1 && Levenshtein(real, name) == 1)
                return $"Its name ({name}) is one character away from the Windows process {real}";
        return null;
    }

    static int Levenshtein(string a, string b)
    {
        a = a.ToLowerInvariant(); b = b.ToLowerInvariant();
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    static readonly string[] DocumentExtensions =
        { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".jpg", ".jpeg", ".png", ".gif", ".mp3", ".mp4", ".zip", ".rar" };

    public static bool HasDoubleExtension(string fileName)
    {
        var withoutLast = Path.GetFileNameWithoutExtension(fileName);
        var inner = Path.GetExtension(withoutLast);
        var outer = Path.GetExtension(fileName).ToLowerInvariant();
        return DocumentExtensions.Contains(inner.ToLowerInvariant()) && outer is ".exe" or ".scr" or ".com" or ".pif" or ".bat" or ".cmd" or ".js" or ".vbs";
    }

    /// <summary>The right-to-left override character hides a real extension (e.g. "photo_exe.jpg" that is really "photogpj.exe").</summary>
    public static bool HasBidiTrick(string fileName) => fileName.Any(c => c is '\u202E' or '\u202D' or '\u2066' or '\u2067');

    public static bool LooksRandom(string nameWithoutExtension)
    {
        if (nameWithoutExtension.Length < 8) return false;
        if (Regex.IsMatch(nameWithoutExtension, @"^[0-9a-fA-F]{16,}$")) return true;
        if (!Regex.IsMatch(nameWithoutExtension, @"^[A-Za-z0-9]+$")) return false;
        var vowels = nameWithoutExtension.Count(c => "aeiouAEIOU".Contains(c));
        var digits = nameWithoutExtension.Count(char.IsDigit);
        return nameWithoutExtension.Length >= 10 && (vowels * 5 < nameWithoutExtension.Length || digits * 3 > nameWithoutExtension.Length);
    }

    public static double Entropy(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return 0;
        Span<int> counts = stackalloc int[256];
        foreach (var b in data) counts[b]++;
        double e = 0;
        foreach (var c in counts)
        {
            if (c == 0) continue;
            var p = (double)c / data.Length;
            e -= p * Math.Log2(p);
        }
        return e;
    }

    // ---- command lines ----

    static readonly (Regex Pattern, string Reason, int Points)[] CommandRules =
    {
        (new(@"powershell(\.exe)?\s.*(-e|-enc|-encodedcommand)\s+[A-Za-z0-9+/=]{30,}", RegexOptions.IgnoreCase), "PowerShell is running an encoded (hidden) command", 55),
        (new(@"-w(indowstyle)?\s+hidden", RegexOptions.IgnoreCase), "It runs with a hidden window", 20),
        (new(@"(invoke-expression|\biex\b\s*[\(\$])|downloadstring|downloadfile|net\.webclient|invoke-webrequest|start-bitstransfer", RegexOptions.IgnoreCase), "It downloads and runs code from the internet", 45),
        (new(@"mshta(\.exe)?\s+(https?:|vbscript:|javascript:)", RegexOptions.IgnoreCase), "mshta is running remote or inline script", 60),
        (new(@"regsvr32.*(/i:https?:|scrobj\.dll)", RegexOptions.IgnoreCase), "regsvr32 is loading remote script (a known bypass trick)", 60),
        (new(@"rundll32.*javascript:", RegexOptions.IgnoreCase), "rundll32 is running inline script", 60),
        (new(@"certutil.*(-urlcache|-decode|-encode)", RegexOptions.IgnoreCase), "certutil is being used to download or decode a file", 45),
        (new(@"bitsadmin.*/transfer", RegexOptions.IgnoreCase), "bitsadmin is downloading a file", 40),
        (new(@"vssadmin.*delete\s+shadows|wmic.*shadowcopy\s+delete|bcdedit.*recoveryenabled\s+no", RegexOptions.IgnoreCase), "It deletes Windows backups (a ransomware signature)", 90),
        (new(@"netsh.*advfirewall.*(set|state).*off", RegexOptions.IgnoreCase), "It switches off the Windows firewall", 50),
        (new(@"(set|add)-mppreference.*(-disable|-exclusion)", RegexOptions.IgnoreCase), "It changes Defender settings to stop scanning", 55),
        (new(@"schtasks.*/create.*(\\temp\\|appdata|\\users\\public)", RegexOptions.IgnoreCase), "It creates a scheduled task that points at a user-writable folder", 40),
        (new(@"(wscript|cscript)(\.exe)?\s+.*(\\temp\\|appdata|\\users\\public)", RegexOptions.IgnoreCase), "A script engine is running a script from a user-writable folder", 35),
    };

    public static IEnumerable<(string Reason, int Points)> CommandLineIndicators(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) yield break;
        foreach (var (pattern, reason, points) in CommandRules)
            if (pattern.IsMatch(commandLine)) yield return (reason, points);
    }

    // ---- scripts ----

    static readonly (Regex Pattern, string Reason, int Points)[] ScriptRules =
    {
        (new(@"[A-Za-z0-9+/=]{400,}"), "Contains a very long encoded blob", 35),
        (new(@"frombase64string", RegexOptions.IgnoreCase), "Decodes hidden data at run time", 25),
        (new(@"(invoke-expression|\biex\b\s*[\(\$])|\beval\s*\(", RegexOptions.IgnoreCase), "Runs text it builds as code", 25),
        (new(@"downloadstring|downloadfile|net\.webclient|invoke-webrequest|msxml2\.xmlhttp|winhttp", RegexOptions.IgnoreCase), "Downloads from the internet", 30),
        (new(@"wscript\.shell.*(powershell|cmd)|-w(indowstyle)?\s+hidden|-ep\s+bypass|-executionpolicy\s+bypass", RegexOptions.IgnoreCase | RegexOptions.Singleline), "Launches other programs quietly", 15),
        (new(@"chr\(\d+\)\s*&\s*chr\(\d+\)|\[char\]\s*\d+\s*\+\s*\[char\]", RegexOptions.IgnoreCase), "Spells out text one character at a time (obfuscation)", 30),
    };

    public static IEnumerable<(string Reason, int Points)> ScriptIndicators(string content)
    {
        foreach (var (pattern, reason, points) in ScriptRules)
            if (pattern.IsMatch(content)) yield return (reason, points);
    }

    // ---- drivers ----

    /// <summary>Drivers that are signed and legitimate but contain flaws that attackers use to gain kernel access ("bring your own vulnerable driver").</summary>
    static readonly HashSet<string> VulnerableDrivers = new(StringComparer.OrdinalIgnoreCase)
    {
        "rtcore64.sys", "rtcore32.sys", "gdrv.sys", "gdrv2.sys", "dbutil_2_3.sys", "dbutildrv2.sys", "iqvw64e.sys", "iqvw32.sys",
        "asio3.sys", "asio64.sys", "asrdrv101.sys", "asrdrv102.sys", "asrdrv103.sys", "asrdrv104.sys", "asrdrv105.sys", "asrdrv106.sys",
        "asmmap.sys", "asmmap64.sys", "winring0x64.sys", "winring0.sys", "cpuz141.sys", "cpuz_x64.sys", "hwrwdrv.sys", "phymem.sys",
        "physmem.sys", "atillk64.sys", "glckio2.sys", "mhyprot2.sys", "mhyprot3.sys", "zam64.sys", "zamguard64.sys", "wnbios.sys",
        "kprocesshacker.sys", "ntiolib_x64.sys", "ntiolib.sys", "gmer64.sys", "bs_def64.sys", "bs_hwmio64.sys", "bs_i2c64.sys",
        "amifldrv64.sys", "msio64.sys", "msio32.sys", "lha.sys", "viragt64.sys", "inpoutx64.sys", "capcom.sys", "speedfan.sys",
    };

    public static bool IsKnownVulnerableDriver(string path) => VulnerableDrivers.Contains(Path.GetFileName(path));
}
