using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Guardian.Services;

public sealed record UpdateInfo(
    Version Version, string Tag, string Notes, string PageUrl,
    string AssetName, string AssetUrl, long Size, string? Sha256);

/// <summary>
/// Checks the project's GitHub releases for a newer installer, downloads it, verifies its
/// SHA-256 against the digest GitHub publishes for the asset, and runs it silently.
/// </summary>
public static class UpdateService
{
    const string Repo = "RivaDeveloper/system-guardian";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Guardian-Updater");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    // Development only: point at a local JSON file shaped like the GitHub "latest release" response
    // (its asset URL may then be a local path) to test the update flow without publishing anything.
    static string? DevFeed
    {
        get
        {
            var v = Environment.GetEnvironmentVariable("GUARDIAN_UPDATE_URL");
            return !string.IsNullOrEmpty(v) && File.Exists(v) ? v : null;
        }
    }

    public static Version CurrentVersion
    {
        get
        {
            var info = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            return Version.TryParse(info.Split('+')[0], out var v) ? v : new Version(0, 0, 0);
        }
    }

    /// <summary>Returns the newest release if it is newer than this build, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        var json = DevFeed is { } file
            ? await File.ReadAllTextAsync(file, ct)
            : await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || version <= CurrentVersion) return null;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (!Regex.IsMatch(name, @"^Guardian-Setup-.+\.exe$")) continue;

            string? sha = null;
            if (asset.TryGetProperty("digest", out var d) && d.GetString() is { } digest && digest.StartsWith("sha256:"))
                sha = digest["sha256:".Length..];

            return new UpdateInfo(
                version, tag,
                root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
                root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "",
                name,
                asset.GetProperty("browser_download_url").GetString() ?? "",
                asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0,
                sha);
        }
        return null;
    }

    /// <summary>Downloads the installer to a temp folder and verifies its checksum. Returns the file path.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (update.Sha256 is null)
            throw new InvalidOperationException(
                "This release has no published checksum, so Guardian won't install it automatically. Download it from the releases page instead.");

        var dir = Path.Combine(Path.GetTempPath(), "Guardian-update");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, update.AssetName);

        if (DevFeed != null && File.Exists(update.AssetUrl))
        {
            File.Copy(update.AssetUrl, path, overwrite: true);
        }
        else
        {
            var uri = new Uri(update.AssetUrl);
            var trustedHost = uri.Host == "github.com" || uri.Host.EndsWith(".github.com");
            if (uri.Scheme != Uri.UriSchemeHttps || !trustedHost)
                throw new InvalidOperationException("The download address is not a GitHub release URL.");

            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? update.Size;

            await using var source = await response.Content.ReadAsStreamAsync(ct);
            await using var target = File.Create(path);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, ct)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        string actual;
        await using (var check = File.OpenRead(path))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(check, ct));

        if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidOperationException("The downloaded file failed its checksum check, so it was discarded.");
        }
        return path;
    }

    /// <summary>
    /// Starts the installer silently (Windows asks for permission if it needs administrator rights).
    /// The installer closes this app, replaces the files and reopens Guardian.
    /// </summary>
    public static void LaunchInstaller(string installerPath)
    {
        var installedForAllUsers = AppContext.BaseDirectory.StartsWith(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)
            || AppContext.BaseDirectory.StartsWith(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), StringComparison.OrdinalIgnoreCase);

        var args = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS" + (installedForAllUsers ? "" : " /CURRENTUSER");
        Process.Start(new ProcessStartInfo(installerPath, args) { UseShellExecute = true });
    }
}
