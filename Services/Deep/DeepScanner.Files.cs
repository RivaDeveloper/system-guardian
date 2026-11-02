using System.Runtime.InteropServices;
using System.Text;

namespace Guardian.Services.Deep;

public sealed partial class DeepScanner
{
    enum CandidateKind { Program, Script, Probe }

    sealed record Candidate(string Path, CandidateKind Kind, bool Hot);

    static readonly HashSet<string> ScriptExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".hta", ".bat", ".cmd" };

    static readonly HashSet<string> LibraryExtensions = new(StringComparer.OrdinalIgnoreCase) { ".dll", ".ocx", ".sys", ".drv" };

    // Types a disguised program might pretend to be. Only the first two bytes are ever read, and only in temp-like folders.
    static readonly HashSet<string> ProbeExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".txt", ".jpg", ".jpeg", ".png", ".gif", ".pdf", ".doc", ".docx", ".dat", ".tmp", ".bin", ".log", ".mp3", ".mp4", ".db", "" };

    /// <summary>
    /// Folders Guardian never goes into: personal files live here, and nothing about a program is hidden there.
    /// This is what keeps ID scans, licences, photos and documents out of the scan entirely.
    /// </summary>
    static readonly string[] PrivateFolders =
    {
        "documents", "my documents", "pictures", "my pictures", "videos", "music", "desktop", "saved games", "contacts",
        "favorites", "links", "searches", "3d objects", "scans", "onedrive", "dropbox", "google drive", "icloud drive",
    };

    // Caches and package folders with huge file counts and no reason to hold a hidden program.
    static readonly string[] NoiseFolders =
    {
        "node_modules", ".git", "cache", "code cache", "gpucache", "shadercache", "crashpad", "service worker", "indexeddb",
        "local storage", "blob_storage", "session storage", "packages", "windowsapps", "guardian",
    };

    static bool SkipFolder(string name) =>
        PrivateFolders.Any(p => name.Equals(p, StringComparison.OrdinalIgnoreCase) || name.StartsWith("onedrive", StringComparison.OrdinalIgnoreCase))
        || NoiseFolders.Contains(name, StringComparer.OrdinalIgnoreCase);

    void SweepFiles()
    {
        Report(1, "Choosing folders to look in", force: true);
        var candidates = new List<Candidate>();

        foreach (var root in HidingPlaces())
        {
            Report(5, "Listing " + root.Dir);
            Walk(root.Dir, 0, root.Recurse, candidates);
        }

        // Top level of the Windows folder and of each drive: programs have no business appearing there.
        foreach (var dir in new[] { Heuristics.WindowsDir }.Concat(DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Select(d => d.RootDirectory.FullName)))
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir))
                {
                    var ext = Path.GetExtension(f);
                    if (Heuristics_IsProgramExt(ext) || ScriptExtensions.Contains(ext)) candidates.Add(new Candidate(f, ScriptExtensions.Contains(ext) ? CandidateKind.Script : CandidateKind.Program, true));
                }
            }
            catch { }
        }

        // System folders: only recent files, since installed Windows files are catalog-signed and long-standing.
        foreach (var dir in new[] { Heuristics.System32, Path.Combine(Heuristics.System32, "drivers"), Heuristics.SysWow64 })
        {
            try
            {
                var cutoff = DateTime.UtcNow.AddDays(-30);
                foreach (var f in Directory.EnumerateFiles(dir))
                    if (PeExtensions.Contains(Path.GetExtension(f)) && File.GetLastWriteTimeUtc(f) > cutoff)
                        candidates.Add(new Candidate(f, CandidateKind.Program, true));
            }
            catch { }
        }

        CandidateCount = candidates.Count;
        var total = Math.Max(1, candidates.Count);
        long done = 0;
        Report(10, $"Checking {candidates.Count:N0} files", force: true);

        try
        {
            Parallel.ForEach(candidates, new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = _ct }, c =>
            {
                try { AssessCandidate(c); } catch (OperationCanceledException) { throw; } catch { }
                var n = Interlocked.Increment(ref done);
                Tick(c.Path, 10 + 90.0 * n / total);
            });
        }
        catch (OperationCanceledException) { throw; }
    }

    static bool Heuristics_IsProgramExt(string ext) => PeExtensions.Contains(ext) && !LibraryExtensions.Contains(ext);

    sealed record Place(string Dir, bool Recurse);

    IEnumerable<Place> HidingPlaces()
    {
        yield return new(Path.Combine(Heuristics.WindowsDir, "Temp"), true);
        yield return new(Path.Combine(Heuristics.WindowsDir, "Tasks"), true);
        yield return new(Path.Combine(Heuristics.UsersDir, "Public"), true);
        yield return new(Heuristics.ProgramData, true);

        // Every profile when running as administrator, otherwise just the signed-in user's.
        IEnumerable<string> profiles;
        if (Elevated)
        {
            try
            {
                profiles = Directory.EnumerateDirectories(Heuristics.UsersDir)
                    .Where(d => !new[] { "Public", "Default", "Default User", "All Users" }.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
            catch { profiles = new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) }; }
        }
        else profiles = new[] { Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };

        foreach (var profile in profiles)
        {
            yield return new(Path.Combine(profile, @"AppData\Roaming"), true);
            yield return new(Path.Combine(profile, @"AppData\Local"), true);
            yield return new(Path.Combine(profile, "Downloads"), true);
        }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
            yield return new(Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin"), true);
    }

    void Walk(string dir, int depth, bool recurse, List<Candidate> output)
    {
        _ct.ThrowIfCancellationRequested();
        if (!Directory.Exists(dir) || depth > 12) return;
        var hotDir = Heuristics.IsHotZone(dir + "\\");
        var tight = Heuristics.IsTightHotZone(dir + "\\");

        try
        {
            foreach (var file in Directory.EnumerateFiles(dir))
            {
                var ext = Path.GetExtension(file);
                if (PeExtensions.Contains(ext))
                {
                    // Libraries are everywhere in app folders; they are only worth a look where nothing should be installed.
                    if (LibraryExtensions.Contains(ext) && !tight) continue;
                    output.Add(new Candidate(file, CandidateKind.Program, hotDir));
                }
                else if (ScriptExtensions.Contains(ext)) output.Add(new Candidate(file, CandidateKind.Script, hotDir));
                else if (tight && ProbeExtensions.Contains(ext)) output.Add(new Candidate(file, CandidateKind.Probe, true));
            }
        }
        catch { }

        if (!recurse) return;
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (SkipFolder(name)) continue;
                try { if (new DirectoryInfo(sub).Attributes.HasFlag(FileAttributes.ReparsePoint)) continue; } catch { continue; }
                Walk(sub, depth + 1, true, output);
            }
        }
        catch { }
    }

    // ---- per-file checks ----

    static readonly byte[] EicarMarker = Encoding.ASCII.GetBytes("EICAR-STANDARD-ANTIVIRUS-TEST-FILE");

    static readonly System.Text.RegularExpressions.Regex InstallerTemp = new(
        @"\\(is-[A-Za-z0-9]+(-uninstall)?\.tmp|ns[sy][0-9A-Fa-f]+\.tmp|~?nsu[^\\]*\.tmp|7z[0-9A-Za-z\-]*|7z-out|\.net|wix[^\\]*|msi[0-9a-f]+\.tmp)\\",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    void AssessCandidate(Candidate c)
    {
        switch (c.Kind)
        {
            case CandidateKind.Program:
            {
                var ev = Assess(c.Path, null, false, skipTrustedLocations: true);
                var stream = SuspiciousStream(c.Path);
                if (stream != null) ev.Add(40, $"It has a hidden data stream attached ({stream})");
                ReportAssessed(FindingKind.File, $"Suspicious program: {Path.GetFileName(c.Path)}", c.Path, ev,
                    c.Path, "Check where it came from. If you don't recognise it, quarantine it, then check it on VirusTotal.");
                break;
            }
            case CandidateKind.Script:
            {
                var ev = new Evidence();
                var loc = Heuristics.Where(c.Path);
                var ext = Path.GetExtension(c.Path);
                // .js files in app folders are almost always web or Electron code that never runs through Windows Script Host.
                var isJs = ext.Equals(".js", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jse", StringComparison.OrdinalIgnoreCase);
                string text = "";
                try
                {
                    var info = new FileInfo(c.Path);
                    if (info.Length is > 0 and < 400_000) text = File.ReadAllText(c.Path);
                }
                catch { }
                if (isJs && !System.Text.RegularExpressions.Regex.IsMatch(text, @"WScript\.|ActiveXObject|Scripting\.FileSystemObject")) return;

                if (loc.Risk >= 20) ev.Add(Math.Min(loc.Risk, 20), $"A script file in {loc.Label}");
                if (Heuristics.HasDoubleExtension(Path.GetFileName(c.Path))) ev.Add(50, "A double extension makes a script look like a document");
                foreach (var (reason, points) in Heuristics.ScriptIndicators(text)) ev.Add(points, reason);
                var stream = SuspiciousStream(c.Path);
                if (stream != null) ev.Add(40, $"It has a hidden data stream attached ({stream})");
                ReportAssessed(FindingKind.File, $"Suspicious script: {Path.GetFileName(c.Path)}", c.Path, ev, c.Path,
                    "Open it in a text editor to see what it does. If you don't recognise it, quarantine it.", threshold: 35);
                break;
            }
            default:
            {
                var ev = new Evidence();
                // Installers (Inno Setup, NSIS, 7-Zip) unpack real programs into temp folders under .tmp names. That is normal.
                if (InstallerTemp.IsMatch(c.Path)) break;
                try
                {
                    var info = new FileInfo(c.Path);
                    if (info.Length is >= 68 and <= 2048)
                    {
                        var head = new byte[(int)info.Length];
                        using var fs = new FileStream(c.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        var n = fs.Read(head, 0, head.Length);
                        if (head.AsSpan(0, n).IndexOf(EicarMarker) >= 0)
                            ev.Add(100, "It is the industry-standard EICAR antivirus test file (harmless, used to check that scanners work)");
                    }
                    if (info.Length >= 4096 && info.Length < 200_000_000)
                    {
                        var two = new byte[2];
                        using var fs = new FileStream(c.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        if (fs.Read(two, 0, 2) == 2 && two[0] == 'M' && two[1] == 'Z')
                        {
                            // A properly signed program that was just given an odd name is an installer artifact, not a disguise.
                            if (Authenticode.Check(c.Path).IsValid) break;
                            ev.Add(65, $"A Windows program disguised as a {Path.GetExtension(c.Path).TrimStart('.').ToUpperInvariant()} file");
                        }
                    }
                }
                catch { }
                ReportAssessed(FindingKind.File, $"Disguised file: {Path.GetFileName(c.Path)}", c.Path, ev, c.Path,
                    "Its contents don't match its name. Quarantine it unless you know why it is here.");
                break;
            }
        }
    }

    // ---- alternate data streams ----

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WIN32_FIND_STREAM_DATA
    {
        public long StreamSize;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)] public string cStreamName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr FindFirstStreamW(string fileName, int infoLevel, out WIN32_FIND_STREAM_DATA data, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool FindNextStreamW(IntPtr handle, out WIN32_FIND_STREAM_DATA data);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool FindClose(IntPtr handle);

    /// <summary>Returns the name of a hidden data stream other than the usual download marker, if any.</summary>
    static string? SuspiciousStream(string path)
    {
        var handle = FindFirstStreamW(path, 0, out var data, 0);
        if (handle == new IntPtr(-1)) return null;
        try
        {
            do
            {
                var name = data.cStreamName;
                if (name == "::$DATA" || name.StartsWith(":Zone.Identifier", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(":SmartScreen", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith(":mshield", StringComparison.OrdinalIgnoreCase)       // set by a security product
                    || name.StartsWith(":encryptable", StringComparison.OrdinalIgnoreCase)    // Windows
                    || name.StartsWith(":favicon", StringComparison.OrdinalIgnoreCase)) continue;
                if (data.StreamSize > 0) return name;
            }
            while (FindNextStreamW(handle, out data));
        }
        finally { FindClose(handle); }
        return null;
    }
}
