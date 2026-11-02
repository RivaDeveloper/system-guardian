using System.Security.Cryptography;
using Guardian.Services.Cleanup;

namespace Guardian.Services.Tools;

public sealed class DupFile
{
    public string Path { get; set; } = "";
    public string Group { get; set; } = "";
    public long Size { get; set; }
    public int GroupId { get; set; }
    public string Subtitle => $"{Group} · {JunkCategory.Format(Size)}";
}

public sealed class FolderEntry
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long Bytes { get; set; }
    public double Percent { get; set; }
    public bool IsFolder { get; set; }
    public string SizeText => JunkCategory.Format(Bytes);
}

public sealed class EmptyFolder
{
    public string Path { get; set; } = "";
}

public static class FileTools
{
    static readonly EnumerationOptions Walk = new()
    {
        RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint,
    };

    /// <summary>Folders where deleting things can break Windows or installed programs; the file tools won't act inside them.</summary>
    public static bool IsProtected(string path)
    {
        var full = System.IO.Path.GetFullPath(path).TrimEnd('\\') + "\\";
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        })
            if (!string.IsNullOrEmpty(root) && full.StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public static Task<List<DupFile>> FindDuplicatesAsync(string folder, Action<string> progress, CancellationToken ct) => Task.Run(() =>
    {
        const long MinSize = 100 * 1024;
        progress("Listing files…");
        var bySize = new Dictionary<long, List<string>>();
        foreach (var f in Directory.EnumerateFiles(folder, "*", Walk))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var len = new FileInfo(f).Length;
                if (len < MinSize) continue;
                if (!bySize.TryGetValue(len, out var l)) bySize[len] = l = new List<string>();
                l.Add(f);
            }
            catch { }
        }

        var candidates = bySize.Where(kv => kv.Value.Count > 1).ToList();
        var groups = new List<DupFile>();
        var done = 0;
        var id = 0;
        foreach (var (size, files) in candidates)
        {
            ct.ThrowIfCancellationRequested();
            progress($"Comparing {++done} of {candidates.Count} size groups…");
            var byHash = new Dictionary<string, List<string>>();
            foreach (var f in files)
            {
                try
                {
                    using var s = File.OpenRead(f);
                    var h = Convert.ToHexString(SHA256.HashData(s));
                    if (!byHash.TryGetValue(h, out var l)) byHash[h] = l = new List<string>();
                    l.Add(f);
                }
                catch { }
            }
            foreach (var g in byHash.Values.Where(l => l.Count > 1))
            {
                id++;
                foreach (var f in g.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    groups.Add(new DupFile { Path = f, Size = size, GroupId = id, Group = $"Set {id}" });
            }
        }
        return groups;
    }, ct);

    public static Task<List<EmptyFolder>> FindEmptyFoldersAsync(string folder, CancellationToken ct) => Task.Run(() =>
    {
        var result = new List<EmptyFolder>();
        // Returns true when the folder holds no files anywhere below it.
        bool Check(string dir, bool isRoot)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) return false;
                var hasFiles = Directory.EnumerateFiles(dir).Any();
                var allEmpty = true;
                var empties = new List<string>();
                foreach (var d in Directory.EnumerateDirectories(dir))
                {
                    if (Check(d, false)) empties.Add(d); else allEmpty = false;
                }
                if (!hasFiles && allEmpty && !isRoot) return true;
                // This folder is kept, so each empty child is reported on its own.
                foreach (var e in empties) result.Add(new EmptyFolder { Path = e });
                return false;
            }
            catch { return false; }
        }
        Check(folder, true);
        return result;
    }, ct);

    public static Task<List<FolderEntry>> AnalyzeAsync(string folder, CancellationToken ct) => Task.Run(() =>
    {
        var entries = new List<FolderEntry>();
        long looseFiles = 0;
        try
        {
            foreach (var f in Directory.EnumerateFiles(folder))
                try { looseFiles += new FileInfo(f).Length; } catch { }
        }
        catch { }

        var dirs = new List<string>();
        try { dirs.AddRange(Directory.EnumerateDirectories(folder)); } catch { }
        var sized = new FolderEntry[dirs.Count];
        Parallel.For(0, dirs.Count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = 4 }, i =>
        {
            long total = 0;
            try
            {
                if ((File.GetAttributes(dirs[i]) & FileAttributes.ReparsePoint) == 0)
                    foreach (var f in Directory.EnumerateFiles(dirs[i], "*", Walk))
                        try { total += new FileInfo(f).Length; } catch { }
            }
            catch { }
            sized[i] = new FolderEntry { Name = System.IO.Path.GetFileName(dirs[i]), Path = dirs[i], Bytes = total, IsFolder = true };
        });
        entries.AddRange(sized);
        if (looseFiles > 0) entries.Add(new FolderEntry { Name = "(files in this folder)", Path = folder, Bytes = looseFiles });
        entries = entries.OrderByDescending(e => e.Bytes).ToList();
        var max = entries.Count > 0 ? Math.Max(1, entries[0].Bytes) : 1;
        foreach (var e in entries) e.Percent = e.Bytes * 100.0 / max;
        return entries;
    }, ct);
}
