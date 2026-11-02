namespace Guardian.Services.Cleanup;

public sealed class JunkCategory
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public long Bytes { get; set; }
    public int Files { get; set; }
    public bool NeedsAdmin { get; set; }
    public List<JunkRoot> Roots { get; } = new();
    public string SizeText => Format(Bytes);
    public string Subtitle => $"{Description} · {Files:N0} file(s)" + (NeedsAdmin ? " · needs administrator" : "");
    public static string Format(long b) =>
        b >= 1L << 30 ? $"{b / (double)(1L << 30):0.0} GB" : b >= 1L << 20 ? $"{b / (double)(1L << 20):0.0} MB" : $"{b / 1024.0:0} KB";
}

public sealed class JunkRoot(string dir, string pattern, bool deep)
{
    public string Dir { get; set; } = dir;
    public string Pattern { get; set; } = pattern;
    public bool Deep { get; set; } = deep;
}

public sealed record JunkResult(long Bytes, int Files, int Locked);

/// <summary>
/// Clears temporary files and caches that Windows and apps rebuild by themselves.
/// Never touches documents, downloads, the recycle bin or anything modified in the last day.
/// </summary>
public sealed class JunkCleaner
{
    static readonly TimeSpan MinAge = TimeSpan.FromHours(24);

    static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    static string Common => Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);

    static IEnumerable<(string name, string desc, bool admin, JunkRoot[] roots)> Definitions()
    {
        yield return ("Temporary files", "Files apps left in your temp folder", false,
            new[] { new JunkRoot(Path.GetTempPath(), "*", true) });
        yield return ("Windows temporary files", "Files Windows left in its temp folder", true,
            new[] { new JunkRoot(Path.Combine(Win, "Temp"), "*", true) });
        var explorer = Path.Combine(Local, @"Microsoft\Windows\Explorer");
        yield return ("Thumbnail cache", "Explorer rebuilds these when needed", false,
            new[] { new JunkRoot(explorer, "thumbcache_*.db", false), new JunkRoot(explorer, "iconcache_*.db", false) });
        yield return ("Crash dumps", "Memory dumps left by crashed programs", false,
            new[] { new JunkRoot(Path.Combine(Local, "CrashDumps"), "*", true), new JunkRoot(Path.Combine(Win, "Minidump"), "*", true) });
        yield return ("Error reports", "Windows Error Reporting archives", false,
            new[] { new JunkRoot(Path.Combine(Local, @"Microsoft\Windows\WER"), "*", true), new JunkRoot(Path.Combine(Common, @"Microsoft\Windows\WER"), "*", true) });
        yield return ("Windows Update downloads", "Update files that are already installed", true,
            new[] { new JunkRoot(Path.Combine(Win, @"SoftwareDistribution\Download"), "*", true) });
        yield return ("Shader caches", "Games and apps rebuild them on next launch", false,
            new[] { new JunkRoot(Path.Combine(Local, "D3DSCache"), "*", true), new JunkRoot(Path.Combine(Local, @"NVIDIA\DXCache"), "*", true), new JunkRoot(Path.Combine(Local, @"AMD\DxCache"), "*", true) });
    }

    public Task<List<JunkCategory>> ScanAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        var result = new List<JunkCategory>();
        foreach (var (name, desc, admin, roots) in Definitions())
        {
            ct.ThrowIfCancellationRequested();
            var cat = new JunkCategory { Name = name, Description = desc, NeedsAdmin = admin };
            foreach (var root in roots)
            {
                if (!Directory.Exists(root.Dir)) continue;
                cat.Roots.Add(root);
                foreach (var f in Enumerate(root))
                {
                    try { cat.Bytes += new FileInfo(f).Length; cat.Files++; } catch { }
                }
            }
            if (cat.Files > 0) result.Add(cat);
        }
        return result;
    }, ct);

    static IEnumerable<string> Enumerate(JunkRoot root)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = root.Deep, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(root.Dir, root.Pattern, opts); } catch { yield break; }
        foreach (var f in files)
        {
            DateTime t;
            try { t = File.GetLastWriteTimeUtc(f); } catch { continue; }
            if (DateTime.UtcNow - t < MinAge) continue;
            yield return f;
        }
    }

    public Task<JunkResult> CleanAsync(IEnumerable<JunkCategory> categories, bool elevated, CancellationToken ct = default) => Task.Run(() =>
    {
        long bytes = 0; int files = 0, locked = 0;
        foreach (var cat in categories)
        {
            if (cat.NeedsAdmin && !elevated) continue;
            foreach (var root in cat.Roots)
            {
                foreach (var f in Enumerate(root).ToList())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var len = new FileInfo(f).Length;
                        File.Delete(f);
                        bytes += len; files++;
                    }
                    catch { locked++; }
                }
                if (root.Deep) RemoveEmptyDirs(root.Dir, true);
            }
        }
        return new JunkResult(bytes, files, locked);
    }, ct);

    static void RemoveEmptyDirs(string dir, bool isRoot)
    {
        try
        {
            foreach (var d in Directory.GetDirectories(dir))
            {
                if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
                RemoveEmptyDirs(d, false);
            }
            if (!isRoot && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
        }
        catch { }
    }
}
