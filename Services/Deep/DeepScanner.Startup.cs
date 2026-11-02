using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Win32;

namespace Guardian.Services.Deep;

public sealed partial class DeepScanner
{
    sealed record StartupEntry(string Source, string Name, string Command);

    void AuditStartup()
    {
        var entries = new List<StartupEntry>();

        Report(2, "Run and RunOnce keys", force: true);
        CollectRunKeys(entries);

        Report(8, "Winlogon, debugger and AppInit settings", force: true);
        AuditWinlogonAndHooks(entries);

        Report(12, "Startup folders and WMI subscriptions", force: true);
        CollectStartupFolders(entries);

        Report(20, "Scheduled tasks", force: true);
        CollectScheduledTasks(entries);

        Report(35, "Services", force: true);
        CollectServices(entries);

        for (var i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            Tick($"{e.Source}: {e.Name}", 40 + 60.0 * (i + 1) / entries.Count);
            try
            {
                var ev = AssessCommand(e.Command);
                var exe = ResolveExecutable(Heuristics_NormalizeCommandPath(e.Command)) ?? "";
                ReportAssessed(FindingKind.Startup, $"Suspicious startup item: {e.Name}", exe.Length > 0 ? exe : e.Command, ev,
                    $"{e.Source}\n{e.Command}", "Check whether you installed this. If not, disable the entry and quarantine the file.");
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }
    }

    static IEnumerable<(string Name, string Value)> RegistryValues(RegistryHive hive, string subKey)
    {
        RegistryKey? key = null;
        try { key = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64).OpenSubKey(subKey); } catch { }
        if (key is null) yield break;
        using (key)
        {
            foreach (var name in key.GetValueNames())
            {
                string? value = null;
                try { value = key.GetValue(name)?.ToString(); } catch { }
                if (!string.IsNullOrWhiteSpace(value)) yield return (name, value);
            }
        }
    }

    void CollectRunKeys(List<StartupEntry> entries)
    {
        var keys = new (RegistryHive Hive, string Path, string Label)[]
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM Run"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM RunOnce"),
            (RegistryHive.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM Run (32-bit)"),
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", "HKLM Policies Run"),
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run"),
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU RunOnce"),
            (RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run", "HKCU Policies Run"),
        };
        foreach (var (hive, path, label) in keys)
            foreach (var (name, value) in RegistryValues(hive, path))
                entries.Add(new StartupEntry(label, name, value));
    }

    void AuditWinlogonAndHooks(List<StartupEntry> entries)
    {
        const string winlogon = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon";
        var shell = Reg(RegistryHive.LocalMachine, winlogon, "Shell") as string;
        if (!string.IsNullOrWhiteSpace(shell) && !shell.Trim().Equals("explorer.exe", StringComparison.OrdinalIgnoreCase))
            Add(HookFinding("winlogon-shell", Severity.High, "Windows starts an unexpected program as its shell",
                $"Winlogon Shell is “{shell}” instead of explorer.exe.", "Whatever is listed here starts with every sign-in.",
                "Set the Shell value back to explorer.exe unless you changed it deliberately."));

        var userinit = Reg(RegistryHive.LocalMachine, winlogon, "Userinit") as string;
        if (!string.IsNullOrWhiteSpace(userinit))
        {
            var cleaned = userinit.Trim().TrimEnd(',').Trim();
            if (!cleaned.Equals(Path.Combine(Heuristics.System32, "userinit.exe"), StringComparison.OrdinalIgnoreCase))
                Add(HookFinding("winlogon-userinit", Severity.High, "Windows starts an extra program at sign-in",
                    $"Winlogon Userinit is “{userinit}”.", "Userinit normally lists only userinit.exe. Extra entries run with every sign-in.",
                    "Restore the default value C:\\Windows\\system32\\userinit.exe, unless you set this on purpose."));
        }

        // Image File Execution Options: a "Debugger" value makes Windows start another program instead of the real one.
        try
        {
            using var ifeo = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options");
            if (ifeo != null)
                foreach (var sub in ifeo.GetSubKeyNames())
                {
                    using var k = ifeo.OpenSubKey(sub);
                    var debugger = k?.GetValue("Debugger") as string;
                    if (string.IsNullOrWhiteSpace(debugger)) continue;

                    // Privacy and "debloat" tools point Microsoft's telemetry programs at a harmless Windows program to stop them.
                    // That is benign. A debugger that is not a signed Windows program, or that targets a security tool, is not.
                    var target = ResolveExecutable(Heuristics_NormalizeCommandPath(debugger));
                    var windowsOwn = target != null && Heuristics.IsInWindowsDir(target) && Authenticode.Check(target).IsValid;
                    var securityTool = Regex.IsMatch(sub, @"(?i)msmpeng|mpcmdrun|msseces|mpdefendercoreservice|nissrv|securityhealth|smartscreen|taskmgr|procexp|regedit|autoruns|wireshark|processhacker|malwarebytes|mbam|avast|kaspersky|bitdefender|eset|^mmc|^cmd\.exe|powershell");

                    if (windowsOwn && !securityTool)
                        Add(HookFinding("ifeo|" + sub, Severity.Low, $"Windows is set to block {sub}",
                            $"Whenever {sub} starts, Windows runs “{debugger}” instead, so it never really runs.",
                            "This is how privacy and “debloat” tools switch off Microsoft telemetry programs. It looks the same as a malware trick, so it is listed.",
                            "Fine if you used such a tool. Otherwise delete the Debugger value under Image File Execution Options."));
                    else
                        Add(HookFinding("ifeo|" + sub, Severity.High, $"{sub} is hijacked by a debugger entry",
                            $"Whenever {sub} starts, Windows runs “{debugger}” instead.",
                            securityTool ? "This redirects a security or system tool to a different program, a classic way to disable protection."
                                         : "The replacement is not a signed Windows program. This is a classic way to persist silently or take over a program.",
                            "Delete the Debugger value under Image File Execution Options unless you are debugging that program."));
                }
        }
        catch { }

        var appInit = Reg(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", "AppInit_DLLs") as string;
        var loadAppInit = Reg(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows", "LoadAppInit_DLLs");
        if (!string.IsNullOrWhiteSpace(appInit) && loadAppInit is int l && l == 1)
            Add(HookFinding("appinit", Severity.High, "A DLL is injected into every program that starts",
                $"AppInit_DLLs lists “{appInit}”.", "Windows loads these DLLs into almost every process. It is a long-standing malware technique.",
                "Clear AppInit_DLLs and set LoadAppInit_DLLs to 0 unless a tool you trust requires it."));
    }

    static Finding HookFinding(string key, Severity severity, string title, string detail, string reason, string recommendation) =>
        new()
        {
            Key = "Startup|" + key, Kind = FindingKind.Startup, Severity = severity, Title = title, Detail = detail,
            Reasons = { reason }, Recommendation = recommendation,
        };

    void CollectStartupFolders(List<StartupEntry> entries)
    {
        var folders = new[] { Environment.GetFolderPath(Environment.SpecialFolder.Startup), Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup) };
        var shortcuts = new List<string>();
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) shortcuts.Add(file);
                else entries.Add(new StartupEntry("Startup folder", name, file));
            }
        }

        // Resolve shortcut targets, and read WMI event subscriptions (a persistence trick that leaves no file in a startup folder).
        string lnkList = string.Join("|", shortcuts.Select(s => s.Replace("'", "''")));
        var json = RunPowerShell(
            "$out=@{lnk=@();wmi=@()};" +
            $"$sh=New-Object -ComObject WScript.Shell;foreach($p in '{lnkList}'.Split('|')){{if($p){{$l=$sh.CreateShortcut($p);$out.lnk+=[pscustomobject]@{{Path=$p;Target=$l.TargetPath;Args=$l.Arguments}}}}}};" +
            "try{$out.wmi=@(Get-CimInstance -Namespace root\\subscription -ClassName __EventConsumer -ErrorAction Stop|Where-Object{$_.CimClass.CimClassName -in 'CommandLineEventConsumer','ActiveScriptEventConsumer'}|ForEach-Object{[pscustomobject]@{Name=[string]$_.Name;Class=$_.CimClass.CimClassName;Command=[string]($_.CommandLineTemplate+$_.ExecutablePath+$_.ScriptText)}})}catch{};" +
            "ConvertTo-Json -Compress -Depth 4 -InputObject $out", _ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("lnk", out var lnk))
                foreach (var l in AsArray(lnk))
                {
                    var target = l.Str("Target");
                    if (target.Length > 0)
                        entries.Add(new StartupEntry("Startup folder shortcut", Path.GetFileNameWithoutExtension(l.Str("Path")), $"\"{target}\" {l.Str("Args")}"));
                }
            if (doc.RootElement.TryGetProperty("wmi", out var wmi))
                foreach (var w in AsArray(wmi))
                {
                    var cmd = w.Str("Command");
                    Add(HookFinding("wmi|" + w.Str("Name"), Severity.High, $"A hidden WMI subscription runs code: {w.Str("Name")}",
                        $"{w.Str("Class")}\n{(cmd.Length > 300 ? cmd[..300] + "…" : cmd)}",
                        "WMI event consumers can run commands or scripts automatically without any startup entry or file in a startup folder.",
                        "Remove the subscription unless a management tool you use created it."));
                }
        }
        catch (JsonException) { }
    }

    static IEnumerable<JsonElement> AsArray(JsonElement e) =>
        e.ValueKind == JsonValueKind.Array ? e.EnumerateArray() : e.ValueKind == JsonValueKind.Object ? new[] { e } : Array.Empty<JsonElement>();

    void CollectScheduledTasks(List<StartupEntry> entries)
    {
        var tasksDir = Path.Combine(Heuristics.System32, "Tasks");
        if (!Directory.Exists(tasksDir)) return;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };

        List<string> files;
        try { files = Directory.EnumerateFiles(tasksDir, "*", options).ToList(); }
        catch { return; }

        var n = 0;
        foreach (var file in files)
        {
            _ct.ThrowIfCancellationRequested();
            n++;
            if (n % 20 == 0) Report(20 + 15.0 * n / files.Count, "Scheduled tasks: " + Path.GetFileName(file));
            try
            {
                var relative = Path.GetRelativePath(tasksDir, file);
                var doc = XDocument.Load(file);
                var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
                if (doc.Descendants(ns + "Enabled").Any(x => x.Value.Equals("false", StringComparison.OrdinalIgnoreCase) && x.Parent?.Name.LocalName == "Settings")) continue;

                foreach (var exec in doc.Descendants(ns + "Exec"))
                {
                    var command = exec.Element(ns + "Command")?.Value?.Trim();
                    if (string.IsNullOrEmpty(command)) continue;
                    var args = exec.Element(ns + "Arguments")?.Value?.Trim() ?? "";
                    var full = $"\"{Environment.ExpandEnvironmentVariables(command).Trim('"')}\" {args}".Trim();

                    // Windows' own tasks that run signed Windows programs are the norm: skip them to keep the list readable.
                    var resolved = ResolveExecutable(Environment.ExpandEnvironmentVariables(command).Trim('"'));
                    if (relative.StartsWith(@"Microsoft\Windows\", StringComparison.OrdinalIgnoreCase) && resolved != null
                        && Heuristics.IsInWindowsDir(resolved) && Authenticode.Check(resolved).IsValid && !Heuristics.CommandLineIndicators(args).Any())
                        continue;
                    entries.Add(new StartupEntry("Scheduled task", relative, full));
                }
            }
            catch { }
        }
    }

    void CollectServices(List<StartupEntry> entries)
    {
        try
        {
            using var services = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services is null) return;
            foreach (var name in services.GetSubKeyNames())
            {
                using var key = services.OpenSubKey(name);
                if (key is null) continue;
                var type = key.GetValue("Type") as int? ?? 0;
                var start = key.GetValue("Start") as int? ?? 4;
                if (start == 4 || (type & 0x30) == 0) continue;   // disabled, or not a user-mode service

                var image = key.GetValue("ImagePath")?.ToString();
                if (string.IsNullOrWhiteSpace(image)) continue;

                if (image.Contains("svchost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    // svchost-hosted services load a DLL: that DLL is what to check.
                    using var parameters = key.OpenSubKey("Parameters");
                    var dll = parameters?.GetValue("ServiceDll")?.ToString();
                    if (!string.IsNullOrWhiteSpace(dll)) entries.Add(new StartupEntry("Service (svchost DLL)", name, dll));
                }
                else
                {
                    entries.Add(new StartupEntry("Service", name, image));
                }
            }
        }
        catch { }
    }
}
