using System.Text;
using Microsoft.Win32;

namespace Guardian.Services.Cleanup;

public sealed class RegistryIssue
{
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Detail { get; set; } = "";
    public bool IsMachine { get; set; }
    public string KeyPath { get; set; } = "";
    /// <summary>Null means the whole key is removed, otherwise just this value.</summary>
    public string? ValueName { get; set; }
    public string Subtitle => $"{Category} · {Detail}";
}

public sealed class RegistryBackup(string folder, DateTime created, int items)
{
    public string Folder { get; set; } = folder;
    public DateTime Created { get; set; } = created;
    public int Items { get; set; } = items;
    public string Text => $"{Created:yyyy-MM-dd HH:mm} · {Items} item(s)";
}

public sealed record RegistryFixResult(int Fixed, int Skipped, string? BackupFolder);

/// <summary>
/// Finds registry entries that point at files which no longer exist. Only well-understood,
/// harmless categories are checked; every fix is backed up to a .reg file first and can be restored.
/// </summary>
public sealed class RegistryCleaner
{
    const string Uninstall = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    const string Uninstall32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";
    const string AppPaths = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths";
    const string SharedDlls = @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs";
    const string Run = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    const string Run32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    const string Mui = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
    const string Fonts = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

    public static readonly string BackupRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guardian", "RegistryBackups");

    static RegistryKey Base(bool machine) =>
        RegistryKey.OpenBaseKey(machine ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);

    public Task<List<RegistryIssue>> ScanAsync(Action<string>? progress = null, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var found = new List<RegistryIssue>();
            void Step(string name, Action a)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Invoke(name);
                try { a(); } catch (Exception ex) when (ex is not OperationCanceledException) { }
            }
            Step("Uninstall entries", () =>
            {
                ScanUninstall(found, true, Uninstall);
                ScanUninstall(found, true, Uninstall32);
                ScanUninstall(found, false, Uninstall);
            });
            Step("Application paths", () => ScanAppPaths(found));
            Step("Shared DLLs", () => ScanValuesAsPaths(found, true, SharedDlls, "Shared DLL", n => n));
            Step("Startup entries", () => { ScanRun(found, true, Run); ScanRun(found, true, Run32); ScanRun(found, false, Run); });
            Step("Program names cache", () => ScanMui(found));
            Step("Fonts", () => ScanFonts(found));
            // HKEY_CLASSES_ROOT is a merged view of these two stores, so each is scanned on its own.
            foreach (var machine in new[] { true, false })
            {
                var classes = machine ? @"SOFTWARE\Classes" : @"Software\Classes";
                Step("File extensions", () => ScanExtensions(found, machine, classes));
                Step("Context menu and file actions", () => ScanVerbs(found, machine, classes));
                Step("COM objects", () =>
                {
                    ScanClsid(found, machine, classes + @"\CLSID", false);
                    if (machine) ScanClsid(found, true, classes + @"\WOW6432Node\CLSID", true);
                });
                Step("Type libraries", () => ScanTypeLibs(found, machine, classes + @"\TypeLib"));
            }
            return found;
        }, ct);

    // ---- HKEY_CLASSES_ROOT ------------------------------------------------------------------------

    static bool ClassKeyExists(string classes, string name)
    {
        foreach (var machine in new[] { true, false })
        {
            using var k = Base(machine).OpenSubKey((machine ? @"SOFTWARE\Classes\" : @"Software\Classes\") + name);
            if (k != null) return true;
        }
        return false;
    }

    /// <summary>Like <see cref="Missing"/>, but never flags anything inside the Windows folder, and honours 32-bit redirection.</summary>
    static bool MissingThirdParty(string path, bool wow)
    {
        if (!Missing(path)) return false;
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (path.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        if (wow)
        {
            var sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (path.StartsWith(sys32 + "\\", StringComparison.OrdinalIgnoreCase)) return false;
            var x86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            if (!string.IsNullOrEmpty(x86) && path.StartsWith(x86 + "\\", StringComparison.OrdinalIgnoreCase) == false
                && path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + "\\", StringComparison.OrdinalIgnoreCase))
            {
                var alt = x86 + path[Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles).Length..];
                if (File.Exists(alt) || Directory.Exists(alt)) return false;
            }
        }
        return true;
    }

    static void ScanExtensions(List<RegistryIssue> found, bool machine, string classes)
    {
        // Windows' own machine-wide defaults deliberately point at file types that aren't registered (Paint, .txt aliases),
        // so only the user's own associations are checked.
        if (machine) return;
        using var root = Base(machine).OpenSubKey(classes);
        if (root == null) return;
        foreach (var name in root.GetSubKeyNames())
        {
            if (!name.StartsWith('.')) continue;
            using var k = root.OpenSubKey(name);
            if (k?.GetValue("") is not string progId || string.IsNullOrWhiteSpace(progId)) continue;
            if (ClassKeyExists(classes, progId)) continue;
            found.Add(new RegistryIssue
            {
                Category = "File extension", Title = name, Detail = $"opens with \"{progId}\", which no longer exists",
                IsMachine = machine, KeyPath = classes + "\\" + name, ValueName = "",
            });
        }
    }

    static void ScanVerbs(List<RegistryIssue> found, bool machine, string classes)
    {
        using var root = Base(machine).OpenSubKey(classes);
        if (root == null) return;
        var shellParents = new List<string>();
        foreach (var name in root.GetSubKeyNames())
        {
            if (name.StartsWith('.') || name.Equals("CLSID", StringComparison.OrdinalIgnoreCase) || name.Equals("TypeLib", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Interface", StringComparison.OrdinalIgnoreCase) || name.Equals("WOW6432Node", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Installer", StringComparison.OrdinalIgnoreCase) || name.Equals("AppID", StringComparison.OrdinalIgnoreCase)) continue;
            shellParents.Add(name + @"\shell");
        }
        shellParents.Add(@"Directory\Background\shell");
        foreach (var parent in shellParents)
        {
            using var shell = root.OpenSubKey(parent);
            if (shell == null) continue;
            foreach (var verb in shell.GetSubKeyNames())
            {
                using var cmdKey = shell.OpenSubKey(verb + @"\command");
                if (cmdKey?.GetValue("") is not string cmd || string.IsNullOrWhiteSpace(cmd)) continue;
                var exe = ExtractPath(cmd);
                if (!MissingThirdParty(exe, false)) continue;
                found.Add(new RegistryIssue
                {
                    Category = "Context menu / file action", Title = $"{parent.Replace(@"\shell", "")} · {verb}",
                    Detail = $"program missing: {exe}", IsMachine = machine, KeyPath = classes + "\\" + parent + "\\" + verb,
                });
            }
        }
    }

    static void ScanClsid(List<RegistryIssue> found, bool machine, string path, bool wow)
    {
        using var root = Base(machine).OpenSubKey(path);
        if (root == null) return;
        foreach (var guid in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(guid);
            if (k == null) continue;
            string? exe = null;
            foreach (var server in new[] { "InprocServer32", "LocalServer32" })
            {
                using var s = k.OpenSubKey(server);
                if (s?.GetValue("") is string cmd && !string.IsNullOrWhiteSpace(cmd)) { exe = ExtractPath(cmd.Trim()); break; }
            }
            if (exe == null || !MissingThirdParty(exe, wow)) continue;
            var title = (k.GetValue("") as string) is { Length: > 0 } n ? n : guid;
            found.Add(new RegistryIssue
            {
                Category = wow ? "COM object (32-bit)" : "COM object", Title = title,
                Detail = $"{guid} · file missing: {exe}", IsMachine = machine, KeyPath = path + "\\" + guid,
            });
        }
    }

    static void ScanTypeLibs(List<RegistryIssue> found, bool machine, string path)
    {
        using var root = Base(machine).OpenSubKey(path);
        if (root == null) return;
        foreach (var guid in root.GetSubKeyNames())
        {
            using var lib = root.OpenSubKey(guid);
            if (lib == null) continue;
            foreach (var ver in lib.GetSubKeyNames())
            {
                using var vk = lib.OpenSubKey(ver);
                if (vk == null) continue;
                var paths = new List<string>();
                foreach (var lcid in vk.GetSubKeyNames())
                {
                    if (!lcid.All(char.IsDigit)) continue;
                    foreach (var plat in new[] { "win32", "win64" })
                    {
                        using var p = vk.OpenSubKey(lcid + "\\" + plat);
                        if (p?.GetValue("") is string f && !string.IsNullOrWhiteSpace(f))
                        {
                            // Paths may end in a resource index, like "foo.dll\2".
                            var file = Environment.ExpandEnvironmentVariables(f.Trim('"'));
                            var cut = file.LastIndexOf('\\');
                            if (cut > 0 && file[(cut + 1)..].All(char.IsDigit) && file[(cut + 1)..].Length > 0) file = file[..cut];
                            paths.Add(file);
                        }
                    }
                }
                // Only when every recorded file is gone, so a library that still has one working copy is never touched.
                if (paths.Count == 0 || !paths.All(p => MissingThirdParty(p, true))) continue;
                found.Add(new RegistryIssue
                {
                    Category = "Type library", Title = (vk.GetValue("") as string) is { Length: > 0 } t ? t : guid,
                    Detail = $"{guid} · file missing: {paths[0]}", IsMachine = machine, KeyPath = path + "\\" + guid + "\\" + ver,
                });
            }
        }
    }

    // ---- scanners ---------------------------------------------------------------------------------

    static void ScanUninstall(List<RegistryIssue> found, bool machine, string path)
    {
        using var root = Base(machine).OpenSubKey(path);
        if (root == null) return;
        foreach (var name in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(name);
            if (k == null) continue;
            if (Convert.ToInt32(k.GetValue("SystemComponent") ?? 0) == 1) continue;
            if (Convert.ToInt32(k.GetValue("WindowsInstaller") ?? 0) == 1) continue;
            if (k.GetValue("ParentKeyName") != null) continue;
            var display = k.GetValue("DisplayName") as string;
            if (string.IsNullOrWhiteSpace(display)) continue;
            var cmd = k.GetValue("UninstallString") as string ?? k.GetValue("QuietUninstallString") as string;
            if (string.IsNullOrWhiteSpace(cmd) || cmd.Contains("msiexec", StringComparison.OrdinalIgnoreCase)) continue;
            var exe = ExtractPath(cmd);
            if (!Missing(exe)) continue;
            // Uninstaller gone but its folder is still there: the program is probably installed, so leave it alone.
            var exeDir = Path.GetDirectoryName(exe);
            if (!string.IsNullOrEmpty(exeDir) && Directory.Exists(exeDir)) continue;
            var loc =(k.GetValue("InstallLocation") as string)?.Trim().Trim('"');
            if (!string.IsNullOrEmpty(loc) && (Directory.Exists(loc) || !DriveAvailable(loc))) continue;
            found.Add(new RegistryIssue
            {
                Category = "Uninstall entry", Title = display!, Detail = $"uninstaller missing: {exe}",
                IsMachine = machine, KeyPath = path + "\\" + name,
            });
        }
    }

    static void ScanAppPaths(List<RegistryIssue> found)
    {
        using var root = Base(true).OpenSubKey(AppPaths);
        if (root == null) return;
        foreach (var name in root.GetSubKeyNames())
        {
            using var k = root.OpenSubKey(name);
            var exe = (k?.GetValue("") as string)?.Trim().Trim('"');
            if (string.IsNullOrEmpty(exe)) continue;
            exe = Environment.ExpandEnvironmentVariables(exe);
            if (!Missing(exe)) continue;
            found.Add(new RegistryIssue
            {
                Category = "Application path", Title = name, Detail = $"program missing: {exe}",
                IsMachine = true, KeyPath = AppPaths + "\\" + name,
            });
        }
    }

    static void ScanValuesAsPaths(List<RegistryIssue> found, bool machine, string path, string category, Func<string, string> toPath)
    {
        using var k = Base(machine).OpenSubKey(path);
        if (k == null) return;
        foreach (var name in k.GetValueNames())
        {
            var file = Environment.ExpandEnvironmentVariables(toPath(name));
            if (!Missing(file)) continue;
            found.Add(new RegistryIssue
            {
                Category = category, Title = Path.GetFileName(file), Detail = $"file missing: {file}",
                IsMachine = machine, KeyPath = path, ValueName = name,
            });
        }
    }

    static void ScanRun(List<RegistryIssue> found, bool machine, string path)
    {
        using var k = Base(machine).OpenSubKey(path);
        if (k == null) return;
        foreach (var name in k.GetValueNames())
        {
            if (k.GetValue(name) is not string cmd || string.IsNullOrWhiteSpace(cmd)) continue;
            var exe = ExtractPath(cmd);
            if (!Missing(exe)) continue;
            found.Add(new RegistryIssue
            {
                Category = "Startup entry", Title = name, Detail = $"program missing: {exe}",
                IsMachine = machine, KeyPath = path, ValueName = name,
            });
        }
    }

    static void ScanMui(List<RegistryIssue> found)
    {
        using var k = Base(false).OpenSubKey(Mui);
        if (k == null) return;
        foreach (var name in k.GetValueNames())
        {
            var file = name.EndsWith(".FriendlyAppName", StringComparison.OrdinalIgnoreCase) ? name[..^16]
                     : name.EndsWith(".ApplicationCompany", StringComparison.OrdinalIgnoreCase) ? name[..^19] : name;
            if (!Missing(file)) continue;
            found.Add(new RegistryIssue
            {
                Category = "Program names cache", Title = Path.GetFileName(file), Detail = $"program missing: {file}",
                IsMachine = false, KeyPath = Mui, ValueName = name,
            });
        }
    }

    static void ScanFonts(List<RegistryIssue> found)
    {
        using var k = Base(true).OpenSubKey(Fonts);
        if (k == null) return;
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        foreach (var name in k.GetValueNames())
        {
            if (k.GetValue(name) is not string file || file.Length == 0) continue;
            file = Environment.ExpandEnvironmentVariables(file);
            var full = Path.IsPathRooted(file) ? file : Path.Combine(dir, file);
            if (!Missing(full)) continue;
            found.Add(new RegistryIssue
            {
                Category = "Font", Title = name, Detail = $"font file missing: {full}",
                IsMachine = true, KeyPath = Fonts, ValueName = name,
            });
        }
    }

    // ---- path helpers -----------------------------------------------------------------------------

    /// <summary>Pulls the program path out of a command line; returns "" when it can't be told.</summary>
    public static string ExtractPath(string command)
    {
        var s = Environment.ExpandEnvironmentVariables(command.Trim());
        if (s.StartsWith('"')) { var end = s.IndexOf('"', 1); return end > 1 ? s[1..end] : ""; }
        var best = -1; var len = 0;
        foreach (var ext in new[] { ".exe", ".dll", ".ocx", ".ax", ".cpl" })
        {
            var i = s.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (i >= 0 && (best < 0 || i < best)) { best = i; len = ext.Length; }
        }
        if (best >= 0) return s[..(best + len)];
        // No recognisable program extension: only trust it when it is a single token.
        return s.Contains(' ') ? "" : s;
    }

    static bool DriveAvailable(string path)
    {
        try { var root = Path.GetPathRoot(path); return !string.IsNullOrEmpty(root) && Directory.Exists(root); }
        catch { return false; }
    }

    /// <summary>True only for a plain local path whose drive is present and where the file really isn't.</summary>
    static bool Missing(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 4 || !Path.IsPathRooted(path) || path.StartsWith(@"\\")) return false;
        if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return false;
        if (!DriveAvailable(path)) return false;
        return !File.Exists(path) && !Directory.Exists(path);
    }

    // ---- fixing and backups -----------------------------------------------------------------------

    public async Task<RegistryFixResult> FixAsync(IReadOnlyList<RegistryIssue> issues, bool elevated, CancellationToken ct = default)
    {
        var todo = issues.Where(i => !i.IsMachine || elevated).ToList();
        var skipped = issues.Count - todo.Count;
        if (todo.Count == 0) return new RegistryFixResult(0, skipped, null);

        var folder = Path.Combine(BackupRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        var fixedCount = 0;
        var n = 0;
        foreach (var issue in todo)
        {
            ct.ThrowIfCancellationRequested();
            var file = Path.Combine(folder, $"{++n:D5}.reg");
            try
            {
                if (!await BackupAsync(issue, file)) { skipped++; continue; }
                using var hive = Base(issue.IsMachine);
                if (issue.ValueName == null) hive.DeleteSubKeyTree(issue.KeyPath, false);
                else { using var k = hive.OpenSubKey(issue.KeyPath, true); k?.DeleteValue(issue.ValueName, false); }
                fixedCount++;
            }
            catch { skipped++; try { File.Delete(file); } catch { } }
        }
        if (fixedCount == 0) { try { Directory.Delete(folder, true); } catch { } return new RegistryFixResult(0, skipped, null); }
        return new RegistryFixResult(fixedCount, skipped, folder);
    }

    static async Task<bool> BackupAsync(RegistryIssue issue, string file)
    {
        var hiveName = issue.IsMachine ? "HKEY_LOCAL_MACHINE" : "HKEY_CURRENT_USER";
        if (issue.ValueName == null)
        {
            var r = await ProcessRunner.RunAsync("reg.exe", $"export \"{(issue.IsMachine ? "HKLM" : "HKCU")}\\{issue.KeyPath}\" \"{file}\" /y");
            return r.ExitCode == 0 && File.Exists(file);
        }
        using var hive = Base(issue.IsMachine);
        using var k = hive.OpenSubKey(issue.KeyPath);
        if (k == null || !k.GetValueNames().Contains(issue.ValueName)) return false;
        var kind = k.GetValueKind(issue.ValueName);
        var value = k.GetValue(issue.ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        var sb = new StringBuilder("Windows Registry Editor Version 5.00\r\n\r\n");
        sb.Append('[').Append(hiveName).Append('\\').Append(issue.KeyPath).Append("]\r\n");
        sb.Append(issue.ValueName.Length == 0 ? "@" : "\"" + Esc(issue.ValueName) + "\"").Append('=');
        switch (kind)
        {
            case RegistryValueKind.String: sb.Append('"').Append(Esc((string)value!)).Append('"'); break;
            case RegistryValueKind.ExpandString: sb.Append("hex(2):").Append(Hex(Encoding.Unicode.GetBytes((string)value! + "\0"))); break;
            case RegistryValueKind.DWord: sb.Append("dword:").Append(((int)value!).ToString("x8")); break;
            case RegistryValueKind.QWord: sb.Append("hex(b):").Append(Hex(BitConverter.GetBytes((long)value!))); break;
            case RegistryValueKind.Binary: sb.Append("hex:").Append(Hex((byte[])value!)); break;
            case RegistryValueKind.MultiString:
                sb.Append("hex(7):").Append(Hex(Encoding.Unicode.GetBytes(string.Join("\0", (string[])value!) + "\0\0"))); break;
            default: return false;
        }
        sb.Append("\r\n");
        await File.WriteAllTextAsync(file, sb.ToString(), Encoding.Unicode);
        return true;
    }

    static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    static string Hex(byte[] b) => string.Join(",", b.Select(x => x.ToString("x2")));

    public List<RegistryBackup> ListBackups()
    {
        var list = new List<RegistryBackup>();
        if (!Directory.Exists(BackupRoot)) return list;
        foreach (var dir in Directory.GetDirectories(BackupRoot).OrderByDescending(d => d))
        {
            var count = Directory.GetFiles(dir, "*.reg").Length;
            if (count > 0) list.Add(new RegistryBackup(dir, Directory.GetCreationTime(dir), count));
        }
        return list;
    }

    public async Task<int> RestoreAsync(RegistryBackup backup, CancellationToken ct = default)
    {
        var ok = 0;
        foreach (var file in Directory.GetFiles(backup.Folder, "*.reg").OrderBy(f => f))
        {
            var r = await ProcessRunner.RunAsync("reg.exe", $"import \"{file}\"", ct: ct);
            if (r.ExitCode == 0) ok++;
        }
        return ok;
    }
}
