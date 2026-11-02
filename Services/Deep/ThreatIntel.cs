using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Guardian.Services.Deep;

/// <summary>Where Guardian keeps the threat data it downloads (hash lists and YARA rules).</summary>
public static class IntelStore
{
    public static string Dir =>
        Environment.GetEnvironmentVariable("GUARDIAN_INTEL_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guardian", "intel");
}

/// <summary>What was downloaded and when. One small file so the UI can show status without loading the data.</summary>
public sealed class IntelMeta
{
    public string LolEtag { get; set; } = "";
    public string IocsEtag { get; set; } = "";
    public DateTime? HashesUpdatedUtc { get; set; }
    public int HashCount { get; set; }
    public string RulesDigest { get; set; } = "";
    public string RulesTag { get; set; } = "";
    public DateTime? RulesUpdatedUtc { get; set; }
    public int RuleCount { get; set; }

    static string File_ => Path.Combine(IntelStore.Dir, "intel.json");

    public static IntelMeta Load()
    {
        try { return File.Exists(File_) ? JsonSerializer.Deserialize<IntelMeta>(File.ReadAllText(File_)) ?? new() : new(); }
        catch { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(IntelStore.Dir);
        File.WriteAllText(File_, JsonSerializer.Serialize(this));
    }
}

public sealed record FileHashes(string Sha256, string Sha1, string Md5);

public sealed record IntelMatch(string Source, string Name, string Kind)
{
    public bool IsVulnerableDriver => Kind == "vulnerable driver";
}

/// <summary>
/// Known-bad file hashes: the LOLDrivers project (drivers abused to take over the kernel) and the signature-base
/// hash list (known malware). Only hashes are compared, locally. Nothing about your files leaves the PC.
/// </summary>
public static class ThreatIntel
{
    const string LolDriversUrl = "https://www.loldrivers.io/api/drivers.json";
    const string IocsUrl = "https://raw.githubusercontent.com/Neo23x0/signature-base/master/iocs/hash-iocs.txt";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(5) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Guardian-Intel");
        return c;
    }

    // ---- the in-memory database ----

    sealed class Db
    {
        public Dictionary<string, string> Sha256 { get; set; } = new();
        public Dictionary<string, string> Sha1 { get; set; } = new();
        public Dictionary<string, string> Md5 { get; set; } = new();
    }

    static Db? _db;
    static readonly object Gate = new();
    static string HashesFile => Path.Combine(IntelStore.Dir, "hashes.json");

    static Db Load()
    {
        lock (Gate)
        {
            if (_db != null) return _db;
            try { _db = File.Exists(HashesFile) ? JsonSerializer.Deserialize<Db>(File.ReadAllText(HashesFile)) ?? new Db() : new Db(); }
            catch { _db = new Db(); }
            return _db;
        }
    }

    public static int Count { get { var d = Load(); return d.Sha256.Count + d.Sha1.Count + d.Md5.Count; } }

    // entries are stored as "name|kind|source"
    static IntelMatch? Parse(string? packed)
    {
        if (packed is null) return null;
        var p = packed.Split('|');
        return p.Length >= 3 ? new IntelMatch(p[2], p[0], p[1]) : null;
    }

    public static IntelMatch? Lookup(FileHashes h)
    {
        var db = Load();
        return Parse(db.Sha256.GetValueOrDefault(h.Sha256)) ?? Parse(db.Sha1.GetValueOrDefault(h.Sha1)) ?? Parse(db.Md5.GetValueOrDefault(h.Md5));
    }

    public static IntelMatch? LookupSha256(string sha256) => Parse(Load().Sha256.GetValueOrDefault(sha256.ToLowerInvariant()));

    /// <summary>A sample hash from the database (used by tests to prove the lookup path works).</summary>
    public static string? AnySha256() => Load().Sha256.Keys.FirstOrDefault();

    // ---- hashing ----

    static readonly ConcurrentDictionary<string, FileHashes?> HashCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>SHA-256, SHA-1 and MD5 in one pass. Skips very large files. Returns null if the file can't be read.</summary>
    public static FileHashes? HashFile(string path, long maxBytes = 200_000_000)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > maxBytes) return null;
            var key = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            return HashCache.GetOrAdd(key, _ => Compute(path));
        }
        catch { return null; }
    }

    static FileHashes? Compute(string path)
    {
        try
        {
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20);
            var buffer = new byte[1 << 20];
            int n;
            while ((n = fs.Read(buffer, 0, buffer.Length)) > 0)
            {
                sha256.AppendData(buffer, 0, n);
                sha1.AppendData(buffer, 0, n);
                md5.AppendData(buffer, 0, n);
            }
            return new FileHashes(
                Convert.ToHexString(sha256.GetHashAndReset()).ToLowerInvariant(),
                Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant(),
                Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant());
        }
        catch { return null; }
    }

    // ---- updating ----

    public static bool IsStale(TimeSpan maxAge)
    {
        var meta = IntelMeta.Load();
        return meta.HashesUpdatedUtc is null || DateTime.UtcNow - meta.HashesUpdatedUtc > maxAge || Count == 0;
    }

    /// <summary>Downloads both hash sources (skipping any that haven't changed) and rebuilds the local index.</summary>
    public static async Task<string> UpdateAsync(IProgress<string>? progress, bool force, CancellationToken ct)
    {
        Directory.CreateDirectory(IntelStore.Dir);
        var meta = IntelMeta.Load();
        var db = force || Count == 0 ? new Db() : Load();
        var changed = false;
        var notes = new List<string>();

        // LOLDrivers (about 36 MB; a conditional request makes repeat updates nearly free)
        progress?.Report("Checking the LOLDrivers list of abused drivers…");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, LolDriversUrl);
            if (!force && meta.LolEtag.Length > 0 && Count > 0) req.Headers.TryAddWithoutValidation("If-None-Match", meta.LolEtag);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (resp.StatusCode == HttpStatusCode.NotModified) notes.Add("LOLDrivers unchanged");
            else
            {
                resp.EnsureSuccessStatusCode();
                progress?.Report("Downloading LOLDrivers…");
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                var fresh = new Db { Sha256 = new(), Sha1 = new(), Md5 = new() };
                ParseLolDrivers(doc.RootElement, fresh);
                // keep any signature-base entries already loaded
                foreach (var kv in db.Sha256.Where(k => k.Value.EndsWith("|signature-base"))) fresh.Sha256[kv.Key] = kv.Value;
                foreach (var kv in db.Sha1.Where(k => k.Value.EndsWith("|signature-base"))) fresh.Sha1[kv.Key] = kv.Value;
                foreach (var kv in db.Md5.Where(k => k.Value.EndsWith("|signature-base"))) fresh.Md5[kv.Key] = kv.Value;
                db = fresh;
                meta.LolEtag = resp.Headers.ETag?.Tag ?? "";
                changed = true;
                notes.Add("LOLDrivers updated");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            notes.Add("LOLDrivers: " + ex.Message);
        }

        // signature-base known-malware hashes
        ct.ThrowIfCancellationRequested();
        progress?.Report("Checking the known-malware hash list…");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, IocsUrl);
            if (!force && meta.IocsEtag.Length > 0 && Count > 0) req.Headers.TryAddWithoutValidation("If-None-Match", meta.IocsEtag);
            using var resp = await Http.SendAsync(req, ct);
            if (resp.StatusCode == HttpStatusCode.NotModified) notes.Add("hash list unchanged");
            else
            {
                resp.EnsureSuccessStatusCode();
                var text = await resp.Content.ReadAsStringAsync(ct);
                foreach (var d in new[] { db.Sha256, db.Sha1, db.Md5 })
                    foreach (var k in d.Where(kv => kv.Value.EndsWith("|signature-base")).Select(kv => kv.Key).ToList()) d.Remove(k);
                ParseIocs(text, db);
                meta.IocsEtag = resp.Headers.ETag?.Tag ?? "";
                changed = true;
                notes.Add("hash list updated");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            notes.Add("hash list: " + ex.Message);
        }

        if (changed)
        {
            var tmp = HashesFile + ".new";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(db), ct);
            File.Move(tmp, HashesFile, overwrite: true);
            lock (Gate) _db = db;
        }
        meta.HashesUpdatedUtc = DateTime.UtcNow;
        meta.HashCount = db.Sha256.Count + db.Sha1.Count + db.Md5.Count;
        meta.Save();

        return $"{meta.HashCount:N0} known-bad hashes ({string.Join(", ", notes)})";
    }

    static void ParseLolDrivers(JsonElement root, Db db)
    {
        if (root.ValueKind != JsonValueKind.Array) return;
        foreach (var driver in root.EnumerateArray())
        {
            var kind = driver.TryGetProperty("Category", out var c) && c.GetString() == "malicious" ? "malicious driver" : "vulnerable driver";
            var tag = "";
            if (driver.TryGetProperty("Tags", out var tags) && tags.ValueKind == JsonValueKind.Array && tags.GetArrayLength() > 0)
                tag = tags[0].GetString() ?? "";
            if (!driver.TryGetProperty("KnownVulnerableSamples", out var samples) || samples.ValueKind != JsonValueKind.Array) continue;

            foreach (var s in samples.EnumerateArray())
            {
                var name = tag;
                if (name.Length == 0 && s.TryGetProperty("ResearchEvidence", out var ev) && ev.ValueKind == JsonValueKind.Object
                    && ev.TryGetProperty("OriginalFilename", out var of)) name = of.GetString() ?? "";
                if (name.Length == 0) name = "driver";
                var packed = $"{name.Replace('|', '/')}|{kind}|LOLDrivers";
                Add(db.Sha256, s, "SHA256", 64, packed);
                Add(db.Sha1, s, "SHA1", 40, packed);
                Add(db.Md5, s, "MD5", 32, packed);
            }
        }
    }

    static void Add(Dictionary<string, string> target, JsonElement sample, string property, int length, string packed)
    {
        if (!sample.TryGetProperty(property, out var v) || v.ValueKind != JsonValueKind.String) return;
        var h = v.GetString()?.Trim().ToLowerInvariant();
        if (h is { } && h.Length == length) target[h] = packed;
    }

    static void ParseIocs(string text, Db db)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split(';', 2);
            var hash = parts[0].Trim().ToLowerInvariant();
            var name = parts.Length > 1 ? parts[1].Trim() : "known malware";
            if (name.Length > 90) name = name[..90];
            var packed = $"{name.Replace('|', '/')}|malware|signature-base";
            switch (hash.Length)
            {
                case 64: db.Sha256[hash] = packed; break;
                case 40: db.Sha1[hash] = packed; break;
                case 32: db.Md5[hash] = packed; break;
            }
        }
    }
}
