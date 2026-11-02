using Guardian.Services.Cleanup;
using Microsoft.Win32;

namespace Guardian.Services.Tools;

public sealed class TrackCategory
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Count { get; set; }
    public long Bytes { get; set; }
    public bool NeedsAdmin { get; set; }
    public string Subtitle => $"{Description} · {Count:N0} item(s)" + (Bytes > 0 ? $", {JunkCategory.Format(Bytes)}" : "") + (NeedsAdmin ? " · needs administrator" : "");
}

/// <summary>
/// Clears the trail Windows and browsers keep of what you opened and typed. Passwords, cookies, saved logins,
/// browsing history, bookmarks and your documents are never touched: browsers only lose their cache.
/// </summary>
public static class TracksService
{
    const string Explorer = @"Software\Microsoft\Windows\CurrentVersion\Explorer";
    static string Roaming => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static string Local => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    static IEnumerable<string> RecentFolders() { yield return Path.Combine(Roaming, @"Microsoft\Windows\Recent"); }

    static IEnumerable<string> JumpListFolders()
    {
        yield return Path.Combine(Roaming, @"Microsoft\Windows\Recent\AutomaticDestinations");
        yield return Path.Combine(Roaming, @"Microsoft\Windows\Recent\CustomDestinations");
    }

    static IEnumerable<string> BrowserCacheFolders()
    {
        var chromium = new[] { @"Google\Chrome\User Data", @"Microsoft\Edge\User Data", @"BraveSoftware\Brave-Browser\User Data", @"Vivaldi\User Data" };
        foreach (var b in chromium)
        {
            var root = Path.Combine(Local, b);
            if (!Directory.Exists(root)) continue;
            foreach (var profile in Directory.EnumerateDirectories(root))
            {
                var n = Path.GetFileName(profile);
                if (n != "Default" && !n.StartsWith("Profile ")) continue;
                foreach (var c in new[] { "Cache", "Code Cache", "GPUCache" }) yield return Path.Combine(profile, c);
            }
        }
        var ff = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
        if (Directory.Exists(ff))
            foreach (var p in Directory.EnumerateDirectories(ff)) yield return Path.Combine(p, "cache2");
    }

    static (int count, long bytes) CountFiles(IEnumerable<string> folders, bool recurse)
    {
        int c = 0; long b = 0;
        var opts = new EnumerationOptions { RecurseSubdirectories = recurse, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var f in folders.Where(Directory.Exists))
            foreach (var file in Directory.EnumerateFiles(f, "*", opts))
                try { b += new FileInfo(file).Length; c++; } catch { }
        return (c, b);
    }

    static int CountValues(string subKey)
    {
        using var k = Registry.CurrentUser.OpenSubKey(subKey);
        if (k == null) return 0;
        return k.GetValueNames().Count(n => n != "MRUList" && n != "MRUListEx") + k.GetSubKeyNames().Length;
    }

    public static Task<List<TrackCategory>> ScanAsync() => Task.Run(() =>
    {
        var list = new List<TrackCategory>();
        void Add(string key, string name, string desc, int count, long bytes = 0, bool admin = false)
        {
            if (count > 0 || admin) list.Add(new TrackCategory { Key = key, Name = name, Description = desc, Count = count, Bytes = bytes, NeedsAdmin = admin });
        }
        var (rc, rb) = CountFiles(RecentFolders(), false);
        Add("recent", "Recent files list", "Shortcuts to files you opened lately", rc, rb);
        var (jc, jb) = CountFiles(JumpListFolders(), false);
        Add("jumplists", "Jump lists", "Recent items under taskbar icons", jc, jb);
        Add("runmru", "Run dialog history", "Commands typed into Win+R", CountValues(Explorer + @"\RunMRU"));
        Add("typedpaths", "Typed paths", "Addresses typed into File Explorer", CountValues(Explorer + @"\TypedPaths"));
        Add("search", "Explorer search history", "Words searched for in File Explorer", CountValues(Explorer + @"\WordWheelQuery"));
        Add("recentdocs", "Recent documents list", "Windows' list of recently used documents", CountValues(Explorer + @"\RecentDocs"));
        var (bc, bb) = CountFiles(BrowserCacheFolders(), true);
        Add("browsercache", "Browser cache", "Cached pages and images (Chrome, Edge, Brave, Vivaldi, Firefox). Logins, history and bookmarks stay.", bc, bb);
        Add("dns", "DNS cache", "Sites your PC recently looked up", 1, 0, true);
        return list;
    });

    public static async Task<int> CleanAsync(IEnumerable<TrackCategory> categories, bool elevated)
    {
        var cleared = 0;
        foreach (var c in categories)
        {
            if (c.NeedsAdmin && !elevated) continue;
            switch (c.Key)
            {
                case "recent":
                    cleared += await Task.Run(() => DeleteFiles(RecentFolders(), false)); break;
                case "jumplists":
                    cleared += await Task.Run(() => DeleteFiles(JumpListFolders(), false)); break;
                case "browsercache":
                    cleared += await Task.Run(() => DeleteFiles(BrowserCacheFolders(), true)); break;
                case "runmru": cleared += ClearValues(Explorer + @"\RunMRU"); break;
                case "typedpaths": cleared += ClearValues(Explorer + @"\TypedPaths"); break;
                case "search": cleared += ClearValues(Explorer + @"\WordWheelQuery"); break;
                case "recentdocs": cleared += ClearValues(Explorer + @"\RecentDocs"); break;
                case "dns":
                    var r = await ProcessRunner.RunAsync("ipconfig.exe", "/flushdns");
                    if (r.ExitCode == 0) cleared++;
                    break;
            }
        }
        return cleared;
    }

    static int DeleteFiles(IEnumerable<string> folders, bool recurse)
    {
        var n = 0;
        var opts = new EnumerationOptions { RecurseSubdirectories = recurse, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        foreach (var f in folders.Where(Directory.Exists))
            foreach (var file in Directory.EnumerateFiles(f, "*", opts).ToList())
                try { File.Delete(file); n++; } catch { }
        return n;
    }

    static int ClearValues(string subKey)
    {
        var n = 0;
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(subKey, true);
            if (k == null) return 0;
            foreach (var v in k.GetValueNames()) { k.DeleteValue(v, false); n++; }
            foreach (var s in k.GetSubKeyNames()) { k.DeleteSubKeyTree(s, false); n++; }
        }
        catch { }
        return n;
    }
}
