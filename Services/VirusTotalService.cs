using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Guardian.Services;

public enum VtVerdict { Clean, Suspicious, Malicious, Unknown, Error }

public sealed class VtResult(string filePath, string sha256, VtVerdict verdict, int malicious, int suspicious, int total, string note)
{
    public string FilePath { get; set; } = filePath;
    public string Sha256 { get; set; } = sha256;
    public VtVerdict Verdict { get; set; } = verdict;
    public int Malicious { get; set; } = malicious;
    public int Suspicious { get; set; } = suspicious;
    public int Total { get; set; } = total;
    public string Note { get; set; } = note;
    public string FileName => Path.GetFileName(FilePath);
    public Microsoft.UI.Xaml.Media.Brush VerdictBrush => Verdict switch
    {
        VtVerdict.Clean => Tones.Good,
        VtVerdict.Suspicious => Tones.Warn,
        VtVerdict.Malicious or VtVerdict.Error => Tones.Bad,
        _ => Tones.Neutral,
    };
    public string VerdictText => Verdict switch
    {
        VtVerdict.Clean => $"Clean ({Total} engines)",
        VtVerdict.Suspicious => $"Suspicious: {Malicious + Suspicious}/{Total} engines",
        VtVerdict.Malicious => $"MALICIOUS: {Malicious}/{Total} engines",
        VtVerdict.Unknown => "Not known to VirusTotal",
        _ => Note,
    };
}

/// <summary>Stores the VirusTotal API key encrypted with DPAPI for the current Windows user.</summary>
public static class ApiKeyStore
{
    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Guardian", "vt.key");

    public static string? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return null; }
    }

    public static void Save(string key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllBytes(FilePath,
            ProtectedData.Protect(Encoding.UTF8.GetBytes(key.Trim()), null, DataProtectionScope.CurrentUser));
    }

    public static void Clear()
    {
        try { File.Delete(FilePath); } catch { }
    }
}

/// <summary>
/// Looks files up on VirusTotal by SHA-256. Files are never uploaded, only their hashes are sent.
/// </summary>
public sealed class VirusTotalService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // The free API tier allows 4 requests/minute.
    static readonly TimeSpan RequestSpacing = TimeSpan.FromSeconds(15.5);

    static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".msi", ".scr", ".sys", ".bat", ".cmd", ".ps1", ".vbs", ".js", ".jar", ".lnk",
    };

    public static IReadOnlyList<FileInfo> FindCandidates(string folder, int max)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        return new DirectoryInfo(folder).EnumerateFiles("*", options)
            .Where(f => Extensions.Contains(f.Extension))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Take(max)
            .ToList();
    }

    public async IAsyncEnumerable<VtResult> ScanAsync(
        IReadOnlyList<FileInfo> files, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var key = ApiKeyStore.Load();
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException("No VirusTotal API key saved. Add one under Settings.");

        var first = true;
        foreach (var file in files)
        {
            if (!first) await Task.Delay(RequestSpacing, ct);
            first = false;
            yield return await CheckFileAsync(file.FullName, key, ct);
        }
    }

    /// <summary>Looks up one file by its hash. Throws if no API key is saved.</summary>
    public static Task<VtResult> CheckOneAsync(string path, CancellationToken ct = default)
    {
        var key = ApiKeyStore.Load();
        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException("No VirusTotal API key saved. Add one under Settings.");
        return CheckFileAsync(path, key, ct);
    }

    static async Task<VtResult> CheckFileAsync(string path, string key, CancellationToken ct)
    {
        string sha;
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
            sha = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new VtResult(path, "", VtVerdict.Error, 0, 0, 0, "Could not read file");
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.virustotal.com/api/v3/files/{sha}");
            req.Headers.Add("x-apikey", key);
            using var resp = await Http.SendAsync(req, ct);

            switch (resp.StatusCode)
            {
                case HttpStatusCode.NotFound:
                    return new VtResult(path, sha, VtVerdict.Unknown, 0, 0, 0, "");
                case HttpStatusCode.Unauthorized:
                    return new VtResult(path, sha, VtVerdict.Error, 0, 0, 0, "Invalid API key");
                case HttpStatusCode.TooManyRequests:
                    return new VtResult(path, sha, VtVerdict.Error, 0, 0, 0, "Rate limit reached, try again later");
            }
            if (!resp.IsSuccessStatusCode)
                return new VtResult(path, sha, VtVerdict.Error, 0, 0, 0, $"HTTP {(int)resp.StatusCode}");

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var stats = doc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("last_analysis_stats");
            int Get(string n) => stats.TryGetProperty(n, out var v) ? v.GetInt32() : 0;
            int mal = Get("malicious"), sus = Get("suspicious"), total = mal + sus + Get("harmless") + Get("undetected");

            // A single engine flagging a file is often a false positive, so require a few.
            var verdict = mal >= 3 ? VtVerdict.Malicious
                        : mal + sus >= 1 ? VtVerdict.Suspicious
                        : VtVerdict.Clean;
            return new VtResult(path, sha, verdict, mal, sus, total, "");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return new VtResult(path, sha, VtVerdict.Error, 0, 0, 0, "Network error");
        }
    }
}
