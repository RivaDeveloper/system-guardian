using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Guardian.Services.Deep;

public sealed partial class DeepScanner
{
    static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
        { "System", "System Idle Process", "Registry", "Memory Compression", "Secure System", "vmmem", "vmmemWSL" };

    static readonly HashSet<string> DocumentApps = new(StringComparer.OrdinalIgnoreCase)
        { "winword.exe", "excel.exe", "powerpnt.exe", "outlook.exe", "onenote.exe", "acrord32.exe", "acrobat.exe", "foxitreader.exe", "wmplayer.exe" };

    static readonly HashSet<string> ScriptHosts = new(StringComparer.OrdinalIgnoreCase)
        { "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe", "regsvr32.exe", "certutil.exe", "bitsadmin.exe" };

    void AuditProcesses()
    {
        Report(2, "Reading the process list", force: true);
        var json = RunPowerShell(
            "$r=@(Get-CimInstance Win32_Process|ForEach-Object{[pscustomobject]@{Id=[int]$_.ProcessId;Parent=[int]$_.ParentProcessId;Name=[string]$_.Name;Path=[string]$_.ExecutablePath;Cmd=[string]$_.CommandLine}});" +
            "ConvertTo-Json -Compress -InputObject $r", _ct);

        var processes = JsonHelper.Items(json).Select(e => (Id: e.Int("Id"), Parent: e.Int("Parent"), Name: e.Str("Name"), Path: e.Str("Path"), Cmd: e.Str("Cmd"))).ToList();
        var byId = processes.GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First());

        // Everything Guardian starts itself (its own PowerShell helpers) is ignored.
        var self = Environment.ProcessId;
        bool IsOurs(int id)
        {
            for (var hops = 0; hops < 12 && byId.TryGetValue(id, out var p); hops++)
            {
                if (p.Id == self) return true;
                id = p.Parent;
            }
            return false;
        }

        var hidden = 0;
        for (var i = 0; i < processes.Count; i++)
        {
            var p = processes[i];
            Tick($"{p.Name} (PID {p.Id})", 5 + 95.0 * (i + 1) / processes.Count);
            if (p.Id <= 4 || IgnoredProcessNames.Contains(p.Name) || IsOurs(p.Id)) continue;

            if (string.IsNullOrEmpty(p.Path)) { hidden++; continue; }   // not readable without administrator rights

            try
            {
                var exePath = p.Path.StartsWith(@"\\?\") ? p.Path[4..] : p.Path;   // extended-length prefix
                var ev = new Evidence();
                ev.Merge(Assess(exePath, p.Cmd, running: true));

                // Documents and readers should not be launching command interpreters.
                if (byId.TryGetValue(p.Parent, out var parent) && DocumentApps.Contains(parent.Name) && ScriptHosts.Contains(p.Name))
                    ev.Add(70, $"It was started by {parent.Name}. Documents launching command interpreters is a classic malware pattern");

                ReportAssessed(FindingKind.Process, $"Suspicious running program: {p.Name}", exePath, ev,
                    $"PID {p.Id}\n{(p.Cmd.Length > 300 ? p.Cmd[..300] + "…" : p.Cmd)}",
                    "If you don't recognise it, end the process and quarantine the file.", p.Id);
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }

        if (hidden > 25 && !Elevated)
            Add(new Finding
            {
                Kind = FindingKind.Process, Severity = Severity.Info, Key = "process-limited",
                Title = $"{hidden} processes could not be inspected", Detail = "Their program files are not readable without administrator rights.",
                Reasons = { "Windows hides other users' and system processes from standard accounts." },
                Recommendation = "Restart Guardian as administrator for a full process check.",
            });
    }

    // ---- kernel drivers ----

    [DllImport("psapi.dll", SetLastError = true)]
    static extern bool EnumDeviceDrivers([Out] IntPtr[] imageBases, uint cb, out uint needed);

    [DllImport("psapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetDeviceDriverFileNameW(IntPtr imageBase, StringBuilder name, uint size);

    static string NormalizeDriverPath(string raw)
    {
        if (raw.StartsWith(@"\??\")) raw = raw[4..];
        if (raw.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) raw = Path.Combine(Heuristics.WindowsDir, raw[12..]);
        if (!Path.IsPathRooted(raw)) raw = Path.Combine(Heuristics.System32, raw);
        return raw;
    }

    static List<string> LoadedKernelModules()
    {
        EnumDeviceDrivers(Array.Empty<IntPtr>(), 0, out var needed);
        if (needed == 0) return new List<string>();
        var bases = new IntPtr[needed / IntPtr.Size];
        if (!EnumDeviceDrivers(bases, needed, out _)) return new List<string>();

        var paths = new List<string>();
        var sb = new StringBuilder(1024);
        foreach (var b in bases)
        {
            if (b == IntPtr.Zero) continue;
            sb.Clear();
            if (GetDeviceDriverFileNameW(b, sb, (uint)sb.Capacity) > 0) paths.Add(NormalizeDriverPath(sb.ToString()));
        }
        return paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    void AuditDrivers()
    {
        Report(2, "Listing loaded kernel modules", force: true);
        var modules = LoadedKernelModules();
        if (modules.Count == 0)
        {
            Add(new Finding
            {
                Kind = FindingKind.Driver, Severity = Severity.Info, Key = "drivers-unreadable", Title = "The kernel module list could not be read",
                Reasons = { "Windows did not return the list of loaded drivers." }, Recommendation = "Restart Guardian as administrator and scan again.",
            });
            return;
        }

        var driverStore = Path.Combine(Heuristics.System32, "DriverStore");
        var driversDir = Path.Combine(Heuristics.System32, "drivers");
        for (var i = 0; i < modules.Count; i++)
        {
            var path = modules[i];
            Tick(Path.GetFileName(path), 5 + 95.0 * (i + 1) / modules.Count);
            try
            {
                var ev = new Evidence();
                if (!File.Exists(path)) { ev.Add(30, "This kernel module is loaded but its file can't be found where Windows says it is"); }
                else
                {
                    var sig = Authenticode.Check(path);
                    if (sig.Status == SignatureStatus.Tampered) ev.Add(95, "The driver's signature no longer matches the file: it was changed after it was signed");
                    else if (sig.Status == SignatureStatus.Untrusted) ev.Add(35, $"Signed by {(sig.Signer.Length > 0 ? sig.Signer : "an unknown publisher")}, but Windows doesn't trust that certificate (expired or unrecognised)");
                    else if (sig.Status == SignatureStatus.Unsigned) ev.Add(70, "An unsigned driver is running inside the Windows kernel");

                    var normalLocation = Heuristics.Under(path, driversDir) || Heuristics.Under(path, driverStore)
                        || Path.GetDirectoryName(path)!.Equals(Heuristics.System32, StringComparison.OrdinalIgnoreCase)
                        || Path.GetDirectoryName(path)!.Equals(Heuristics.WindowsDir, StringComparison.OrdinalIgnoreCase);
                    if (!normalLocation) ev.Add(25, $"Loaded from an unusual place: {Path.GetDirectoryName(path)}");

                    if (Heuristics.IsKnownVulnerableDriver(path))
                        ev.Add(45, "A known-vulnerable driver. It is legitimately signed (often by hardware tools like MSI Afterburner, CPU-Z or HWiNFO) but is widely abused to take control of the kernel and switch off security software");
                }

                ReportAssessed(FindingKind.Driver, $"Kernel driver needs attention: {Path.GetFileName(path)}", path, ev, path,
                    "Unload and remove the driver if you don't know which program installed it.");
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
    }
}
