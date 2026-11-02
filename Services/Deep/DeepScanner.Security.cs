using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Guardian.Services.Deep;

public sealed partial class DeepScanner
{
    static object? Reg(RegistryHive hive, string subKey, string name)
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(subKey);
            return key?.GetValue(name);
        }
        catch { return null; }
    }

    static string RunPowerShell(string script, CancellationToken ct) =>
        ProcessRunner.RunPowerShellAsync(script, ct: ct).GetAwaiter().GetResult().Output;

    void SecurityFinding(string key, Severity severity, string title, string detail, string reason, string recommendation) =>
        Add(new Finding
        {
            Key = "Security|" + key, Kind = FindingKind.Security, Severity = severity, Title = title, Detail = detail,
            Reasons = { reason }, Recommendation = recommendation,
        });

    void SecurityFinding(string key, Severity severity, string title, string detail, string reason, string secondReason, string recommendation) =>
        Add(new Finding
        {
            Key = "Security|" + key, Kind = FindingKind.Security, Severity = severity, Title = title, Detail = detail,
            Reasons = { reason, secondReason }, Recommendation = recommendation,
        });

    void AuditSecurity()
    {
        const int steps = 9;
        var step = 0;
        void Next(string item) => Tick(item, ++step * 100.0 / steps);

        // 1. Windows Defender settings and exclusions: attackers add exclusions so their files are never scanned.
        Next("Windows Defender settings and exclusions");
        try
        {
            var json = RunPowerShell(
                "$p=Get-MpPreference;$s=Get-MpComputerStatus;ConvertTo-Json -Compress -InputObject ([pscustomobject]@{" +
                "RT=[bool]$p.DisableRealtimeMonitoring;Beh=[bool]$p.DisableBehaviorMonitoring;Av=[bool]$s.AntivirusEnabled;" +
                "Paths=@($p.ExclusionPath);Procs=@($p.ExclusionProcess);Exts=@($p.ExclusionExtension)})", _ct);
            var item = JsonHelper.Items(json).FirstOrDefault();
            if (item.ValueKind == JsonValueKind.Object)
            {
                if (item.Bool("RT") && item.Bool("Av"))
                    SecurityFinding("defender-rt", Severity.High, "Defender real-time protection is switched off",
                        "Files are not being scanned as they are opened or downloaded.",
                        "Real-time protection is disabled in Windows Security.", "Turn real-time protection back on in Windows Security.");
                if (item.Bool("Beh") && item.Bool("Av"))
                    SecurityFinding("defender-behavior", Severity.Medium, "Defender behavior monitoring is switched off",
                        "Defender is not watching what programs do while they run.", "Behavior monitoring is disabled.",
                        "Turn behavior monitoring back on in Windows Security.");

                foreach (var path in Strings(item, "Paths"))
                {
                    var broad = Regex.IsMatch(path, @"^[A-Za-z]:\\?$") || Regex.IsMatch(path, @"(?i)\\users\\?$|\\temp\\?$|\\appdata|\\downloads\\?$|\\programdata\\?$|\\users\\public|^[A-Za-z]:\\windows");
                    SecurityFinding("excl-path|" + path.ToLowerInvariant(), broad ? Severity.High : Severity.Low,
                        broad ? "Defender is told to ignore a folder malware likes" : "Defender skips a folder",
                        path, broad
                            ? "Anything placed in this folder is never scanned. Malware often adds exactly this kind of exclusion."
                            : "Files in this folder are never scanned. That's common for game or developer folders.",
                        "Excluded from scanning: " + path,
                        broad ? "Remove this exclusion unless you added it on purpose." : "Make sure you added this exclusion yourself.");
                }
                foreach (var ext in Strings(item, "Exts"))
                {
                    var risky = Regex.IsMatch(ext, @"(?i)^\.?(exe|dll|ps1|bat|cmd|vbs|js|scr|hta)$");
                    SecurityFinding("excl-ext|" + ext.ToLowerInvariant(), risky ? Severity.High : Severity.Low,
                        "Defender ignores a file type: " + ext, ext,
                        risky ? "Programs and scripts of this type are never scanned anywhere on the PC." : "Files of this type are never scanned.",
                        "Excluded file type: " + ext, "Remove this exclusion unless you added it on purpose.");
                }
                foreach (var proc in Strings(item, "Procs"))
                    SecurityFinding("excl-proc|" + proc.ToLowerInvariant(), Severity.Medium, "Defender ignores a program: " + proc, proc,
                        "Whatever this program does is never scanned.", "Excluded process: " + proc,
                        "Remove this exclusion unless you added it on purpose.");
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* Defender cmdlets unavailable (another antivirus manages protection) */ }

        // 2. Firewall
        Next("Windows Firewall");
        foreach (var profile in new[] { "StandardProfile", "PublicProfile", "DomainProfile" })
        {
            var v = Reg(RegistryHive.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\{profile}", "EnableFirewall");
            if (v is int i && i == 0)
                SecurityFinding("firewall|" + profile, Severity.Medium, $"Windows Firewall is off ({profile.Replace("Profile", "").ToLower()} networks)",
                    "Network connections are not being filtered.", "EnableFirewall is 0 for this profile.", "Turn the firewall back on in Windows Security.");
        }

        // 3. User Account Control
        Next("User Account Control");
        const string policies = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
        if (Reg(RegistryHive.LocalMachine, policies, "EnableLUA") is int lua && lua == 0)
            SecurityFinding("uac-off", Severity.High, "User Account Control is turned off",
                "Programs can gain administrator rights without asking you.", "EnableLUA is 0.", "Turn UAC back on in Windows settings, then restart.");
        else if (Reg(RegistryHive.LocalMachine, policies, "ConsentPromptBehaviorAdmin") is int consent && consent == 0)
            SecurityFinding("uac-silent", Severity.Medium, "UAC never asks before giving administrator rights",
                "Administrator actions run without a prompt.", "ConsentPromptBehaviorAdmin is 0.", "Set UAC to at least the default level.");

        // 4. Boot configuration: test signing and disabled integrity checks let unsigned drivers load.
        Next("Boot configuration");
        if (Elevated)
        {
            try
            {
                var output = ProcessRunner.RunAsync("bcdedit.exe", "/enum {current}", ct: _ct).GetAwaiter().GetResult().Output;
                if (Regex.IsMatch(output, @"(?im)^\s*testsigning\s+Yes"))
                    SecurityFinding("testsigning", Severity.High, "Windows is in test-signing mode",
                        "Unsigned kernel drivers are allowed to load.", "Boot setting testsigning is Yes.", "Run “bcdedit /set testsigning off” as administrator and restart.");
                if (Regex.IsMatch(output, @"(?im)^\s*nointegritychecks\s+Yes"))
                    SecurityFinding("nointegrity", Severity.Critical, "Driver integrity checks are disabled",
                        "Windows will load drivers without verifying their signature.", "Boot setting nointegritychecks is Yes.", "Run “bcdedit /set nointegritychecks off” as administrator and restart.");
                if (Regex.IsMatch(output, @"(?im)^\s*debug\s+Yes"))
                    SecurityFinding("kerneldebug", Severity.Medium, "Kernel debugging is enabled at boot",
                        "A debugger can inspect or change the running system.", "Boot setting debug is Yes.", "Turn it off unless you are developing drivers.");
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        // 5. Secure Boot, memory integrity, vulnerable driver blocklist
        Next("Secure Boot and driver protection");
        var secureBoot = Reg(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\SecureBoot\State", "UEFISecureBootEnabled");
        if (secureBoot is int sb && sb == 0)
            SecurityFinding("secureboot", Severity.Low, "Secure Boot is off",
                "The PC does not verify that the boot chain is untampered. Many anti-cheat systems require it.",
                "UEFISecureBootEnabled is 0.", "Turn on Secure Boot in your UEFI firmware settings if your system supports it.");

        var hvci = Reg(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled");
        if (hvci is not int h || h == 0)
            SecurityFinding("hvci", Severity.Low, "Memory integrity (core isolation) is off",
                "Vulnerable or malicious drivers are easier to load.", "HypervisorEnforcedCodeIntegrity is not enabled.",
                "Turn on Memory integrity in Windows Security > Device security > Core isolation.");

        if (Reg(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\CI\Config", "VulnerableDriverBlocklistEnable") is int bl && bl == 0)
            SecurityFinding("blocklist", Severity.Medium, "The vulnerable driver blocklist is off",
                "Windows will not block drivers that are known to be abused to take over the kernel.",
                "VulnerableDriverBlocklistEnable is 0.", "Turn the blocklist on in Windows Security > Core isolation.");

        // 6. hosts file
        Next("hosts file");
        try
        {
            var hosts = Path.Combine(Heuristics.System32, @"drivers\etc\hosts");
            if (File.Exists(hosts))
            {
                var lines = File.ReadAllLines(hosts).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToList();
                var securityHosts = lines.Where(l => Regex.IsMatch(l, @"(?i)defender|windowsupdate|update\.microsoft|virustotal|kaspersky|malwarebytes|avast|avg\.com|eset|bitdefender|norton|mcafee|sophos|trendmicro|smartscreen")).ToList();
                if (securityHosts.Count > 0)
                    SecurityFinding("hosts-av", Severity.High, "The hosts file blocks security or update sites",
                        string.Join("\n", securityHosts.Take(5)), "Entries redirect antivirus or Windows Update addresses (malware does this to stop updates).",
                        "Remove those lines from C:\\Windows\\System32\\drivers\\etc\\hosts.");
                else if (lines.Count > 25)
                    SecurityFinding("hosts-many", Severity.Info, $"The hosts file has {lines.Count} custom entries",
                        "That is more than usual.", "Large hosts files are used for ad blocking, but can also redirect sites.", "Review the file if you didn't add these.");
            }
        }
        catch { }

        // 7. proxy settings
        Next("Proxy settings");
        var proxyOn = Reg(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyEnable");
        var proxyServer = Reg(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "ProxyServer") as string;
        var autoConfig = Reg(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Internet Settings", "AutoConfigURL") as string;
        if ((proxyOn is int p && p == 1 && !string.IsNullOrEmpty(proxyServer)) || !string.IsNullOrEmpty(autoConfig))
            SecurityFinding("proxy", Severity.Medium, "Your internet traffic goes through a proxy",
                proxyServer ?? autoConfig ?? "", "A proxy is configured. That's normal for some VPNs and offices, but malware also sets one to watch or redirect traffic.",
                "Make sure you set this up yourself, or turn the proxy off in Settings > Network > Proxy.");

        // 8. remote access
        Next("Remote access");
        if (Reg(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Terminal Server", "fDenyTSConnections") is int rdp && rdp == 0)
            SecurityFinding("rdp", Severity.Low, "Remote Desktop is enabled",
                "Someone with your password could connect to this PC over the network.", "fDenyTSConnections is 0.",
                "Turn Remote Desktop off in Settings > System > Remote Desktop if you don't use it.");

        Next("Done");
    }

    static IEnumerable<string> Strings(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array) yield break;
        foreach (var v in arr.EnumerateArray())
        {
            var s = v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (!string.IsNullOrWhiteSpace(s) && !s.StartsWith("N/A", StringComparison.OrdinalIgnoreCase)) yield return s;
        }
    }
}
