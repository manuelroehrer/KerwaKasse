using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using KerwaKasse.Core.Services;
using Microsoft.Extensions.Logging;

namespace KerwaKasse.Core.Data;

public class GitHubUpdateService : IUpdateService
{
    // One shared client without a global timeout: the short timeout below must only apply to the
    // release check, not cut off a slow installer download mid-way.
    private static readonly HttpClient Http = CreateClient();
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

    // Releases are created by hand, so the version is fished out of the tag or title leniently:
    // the first thing looking like a version number counts, prefixes ("v"), surrounding text and
    // suffixes ("-beta.1") are ignored.
    private static readonly Regex VersionPattern = new(@"\d+\.\d+(\.\d+){0,2}", RegexOptions.Compiled);

    private readonly string _latestReleaseUrl;
    private readonly ILogger<GitHubUpdateService> _logger;

    /// <param name="repositoryUrl">The public GitHub repository, e.g. "https://github.com/user/repo".</param>
    public GitHubUpdateService(string repositoryUrl, ILogger<GitHubUpdateService> logger)
    {
        _logger = logger;
        // "https://github.com/user/repo" → "https://api.github.com/repos/user/repo/releases/latest".
        // This endpoint returns the newest published release; drafts and pre-releases are excluded.
        string ownerAndRepo = new Uri(repositoryUrl).AbsolutePath.Trim('/');
        _latestReleaseUrl = $"https://api.github.com/repos/{ownerAndRepo}/releases/latest";
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("KerwaKasse");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(CheckTimeout);

            string json = await Http.GetStringAsync(_latestReleaseUrl, timeout.Token);
            var result = ParseLatestRelease(json, currentVersion);

            if (result.Status == UpdateCheckStatus.UpdateAvailable)
                _logger.LogInformation("Update available: {Latest} (running {Current})", result.Update!.Version, Normalize(currentVersion));
            else if (result.Status == UpdateCheckStatus.UpToDate)
                _logger.LogDebug("No update available");
            else
                _logger.LogInformation("Update check inconclusive: no version found in the latest release");

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Expected whenever there is no internet connection or the repository moved; by design
            // this stays a quiet log line, never a user-facing error.
            _logger.LogInformation("Update check failed (offline or repository unreachable): {Reason}", ex.Message);
            return new UpdateCheckResult(UpdateCheckStatus.Failed);
        }
    }

    /// <summary>Turns the JSON of GitHub's "latest release" endpoint into a check result.
    /// Public and static so the lenient parsing is unit-testable without network access.</summary>
    public static UpdateCheckResult ParseLatestRelease(string json, Version currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        var release = document.RootElement;

        Version? latest = ExtractVersion(GetString(release, "tag_name")) ?? ExtractVersion(GetString(release, "name"));
        if (latest is null)
            return new UpdateCheckResult(UpdateCheckStatus.Failed);

        if (latest <= Normalize(currentVersion))
            return new UpdateCheckResult(UpdateCheckStatus.UpToDate);

        var (installerUrl, installerSize) = PickInstallerAsset(release);
        var update = new UpdateInfo(latest, GetString(release, "body"), GetString(release, "html_url"), installerUrl, installerSize);
        return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, update);
    }

    /// <summary>The first version-looking number in <paramref name="text"/>, normalized to three parts,
    /// or null when there is none.</summary>
    public static Version? ExtractVersion(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = VersionPattern.Match(text);
        return match.Success && Version.TryParse(match.Value, out var version) ? Normalize(version) : null;
    }

    // Comparisons must not depend on how many parts a version was written with: assembly versions
    // carry four parts (2.9.0.0), tags usually three (v2.9.0), sometimes two (v2.9). Everything is
    // reduced to major.minor.build with missing parts as zero.
    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    // The installer is the release's .exe asset. Matching is deliberately loose (any .exe counts,
    // one named like "setup" wins) so a renamed installer keeps working.
    private static (string? Url, long Size) PickInstallerAsset(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return (null, 0);

        (string? Url, long Size) firstExe = (null, 0);
        foreach (var asset in assets.EnumerateArray())
        {
            string? name = GetString(asset, "name");
            if (name is null || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;

            string? url = GetString(asset, "browser_download_url");
            long size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;

            if (name.Contains("setup", StringComparison.OrdinalIgnoreCase))
                return (url, size);
            if (firstExe.Url is null)
                firstExe = (url, size);
        }
        return firstExe;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public async Task<string> DownloadInstallerAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (update.InstallerUrl is null)
            throw new InvalidOperationException("The release carries no installer asset.");

        string downloadDir = Path.Combine(Path.GetTempPath(), "KerwaKasse_Update");
        Directory.CreateDirectory(downloadDir);
        string fileName = Path.GetFileName(new Uri(update.InstallerUrl).LocalPath);
        if (string.IsNullOrEmpty(fileName)) fileName = "KerwaKasse_Setup.exe";
        string filePath = Path.Combine(downloadDir, fileName);

        using var response = await Http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? update.InstallerSize;
        await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var target = File.Create(filePath))
        {
            var buffer = new byte[81920];
            long written = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                written += read;
                if (totalBytes > 0) progress?.Report((double)written / totalBytes);
            }
        }

        // Plausibility check against the size the release metadata announced, so a truncated
        // download is never handed to the installer.
        long actualSize = new FileInfo(filePath).Length;
        if (update.InstallerSize > 0 && actualSize != update.InstallerSize)
            throw new IOException($"Downloaded installer is incomplete ({actualSize} of {update.InstallerSize} bytes).");

        _logger.LogInformation("Installer for {Version} downloaded to {FilePath}", update.Version, filePath);
        return filePath;
    }
}
