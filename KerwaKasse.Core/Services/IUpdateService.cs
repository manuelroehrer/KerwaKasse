namespace KerwaKasse.Core.Services;

/// <summary>Checks the project's GitHub releases for a newer version and downloads its installer.
/// The check is best-effort: no internet connection or an unreachable repository is a normal
/// outcome (<see cref="UpdateCheckStatus.Failed"/>), never an error surfaced to the user.</summary>
public interface IUpdateService
{
    /// <summary>Fetches the published releases and compares them against
    /// <paramref name="currentVersion"/>. Never throws.</summary>
    Task<UpdateCheckResult> CheckForUpdateAsync(Version currentVersion, CancellationToken cancellationToken = default);

    /// <summary>Downloads the installer of <paramref name="update"/> to a local temp folder and returns
    /// its path. <paramref name="progress"/> reports 0..1. Throws on failure or size mismatch.</summary>
    Task<string> DownloadInstallerAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

public enum UpdateCheckStatus
{
    /// <summary>A release newer than the running version was found.</summary>
    UpdateAvailable,
    /// <summary>The latest release is the running version (or older).</summary>
    UpToDate,
    /// <summary>The check could not be completed — offline, repository unreachable or an
    /// unparseable release. Treated silently except for an explicit manual check.</summary>
    Failed
}

/// <summary>Outcome of an update check. <see cref="Update"/> is set only for
/// <see cref="UpdateCheckStatus.UpdateAvailable"/>.</summary>
public record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Update = null);

/// <summary>The newest released version. <see cref="InstallerUrl"/> is null when the release carries no
/// .exe asset — the UI then falls back to opening <see cref="ReleaseUrl"/> in the browser.
/// <see cref="ReleaseNotes"/> holds every release newer than the running version, this one included,
/// newest first, so a user who skipped several versions sees all changes at once.</summary>
public record UpdateInfo(Version Version, IReadOnlyList<ReleaseNote> ReleaseNotes, string? ReleaseUrl, string? InstallerUrl, long InstallerSize);

/// <summary>The hand-written notes (Markdown) of one release.</summary>
public record ReleaseNote(Version Version, string? Text, string? ReleaseUrl, DateTimeOffset? PublishedAt);
