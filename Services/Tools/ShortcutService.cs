using Microsoft.VisualBasic.FileIO;

namespace Guardian.Services.Tools;

public sealed class ShortcutItem
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    /// <summary>Where the program seems to have moved to, when exactly one file of the same name is nearby.</summary>
    public string? NewTarget { get; set; }
    public string Subtitle => NewTarget != null ? $"Can be repaired: {Target}  →  {NewTarget}" : $"Target is gone: {Target}";
}

/// <summary>Finds shortcuts on the desktop and in the Start menu whose target no longer exists, and relinks or removes them.</summary>
public static class ShortcutService
{
    static IEnumerable<string> Folders()
    {
        var f = Environment.SpecialFolder.Desktop;
        yield return Environment.GetFolderPath(f);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
        yield return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch");
    }

    static dynamic NewShell() => Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;

    public static Task<List<ShortcutItem>> ScanAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var result = new List<ShortcutItem>();
        dynamic shell = NewShell();
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var dir in Folders().Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.lnk", opts))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    dynamic lnk = shell.CreateShortcut(file);
                    string target = lnk.TargetPath ?? "";
                    if (!IsBroken(target)) continue;
                    result.Add(new ShortcutItem
                    {
                        Path = file, Name = System.IO.Path.GetFileNameWithoutExtension(file), Target = target, NewTarget = FindMoved(target),
                    });
                }
                catch { }
            }
        }
        return result.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }, ct);

    static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);

    static bool IsBroken(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || target.Contains("://") || !System.IO.Path.IsPathRooted(target) || target.StartsWith(@"\\")) return false;
        var root = System.IO.Path.GetPathRoot(target);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return false; // drive not connected
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (target.StartsWith(win + "\\", StringComparison.OrdinalIgnoreCase)) return false;
        if (Exists(target)) return false;
        // 32-bit and 64-bit Program Files are easy to mix up.
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(pf86))
        {
            if (target.StartsWith(pf + "\\", StringComparison.OrdinalIgnoreCase) && Exists(pf86 + target[pf.Length..])) return false;
            if (target.StartsWith(pf86 + "\\", StringComparison.OrdinalIgnoreCase) && Exists(pf + target[pf86.Length..])) return false;
        }
        return true;
    }

    /// <summary>Looks near where the target used to be (apps that update into a new version folder) for exactly one file of the same name.</summary>
    static string? FindMoved(string target)
    {
        try
        {
            var name = System.IO.Path.GetFileName(target);
            if (string.IsNullOrEmpty(name)) return null;
            var dir = System.IO.Path.GetDirectoryName(target);
            var steps = 0;
            while (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir) && steps++ < 4) dir = System.IO.Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(dir) || dir.Length <= 3) return null; // never search a whole drive
            var depth = dir.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
            if (depth < 2) return null;
            var matches = new List<string>();
            var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 4, AttributesToSkip = FileAttributes.ReparsePoint };
            var seen = 0;
            foreach (var f in Directory.EnumerateFiles(dir, name, opts))
            {
                matches.Add(f);
                if (matches.Count > 1 || ++seen > 50) break;
            }
            return matches.Count == 1 ? matches[0] : null;
        }
        catch { return null; }
    }

    public static bool Repair(ShortcutItem item)
    {
        if (item.NewTarget == null) return false;
        try
        {
            dynamic shell = NewShell();
            dynamic lnk = shell.CreateShortcut(item.Path);
            string work = lnk.WorkingDirectory ?? "";
            lnk.TargetPath = item.NewTarget;
            if (work.Length == 0 || !Directory.Exists(work)) lnk.WorkingDirectory = System.IO.Path.GetDirectoryName(item.NewTarget);
            lnk.Save();
            return true;
        }
        catch { return false; }
    }

    public static bool Remove(ShortcutItem item)
    {
        try { FileSystem.DeleteFile(item.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin); return true; }
        catch { return false; }
    }
}
