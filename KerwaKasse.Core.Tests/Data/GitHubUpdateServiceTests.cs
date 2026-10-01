using KerwaKasse.Core.Data;
using KerwaKasse.Core.Services;

namespace KerwaKasse.Core.Tests.Data;

public class GitHubUpdateServiceTests
{
    // --- ExtractVersion: releases are created by hand, so parsing must forgive prefix, casing,
    // surrounding text, missing parts and suffixes. ---

    [Theory]
    [InlineData("v2.9.0", "2.9.0")]
    [InlineData("2.9.0", "2.9.0")]
    [InlineData("V2.10.1", "2.10.1")]
    [InlineData("v.2.9.0", "2.9.0")]
    [InlineData("KerwaKasse v2.9.0", "2.9.0")]
    [InlineData("Kerverkasse v2.9.0", "2.9.0")]
    [InlineData("release-2.9", "2.9.0")]
    [InlineData("v2.10.0-beta.1", "2.10.0")]
    [InlineData("v2.9.0.1", "2.9.0")]
    public void ExtractVersion_FindsVersionInLenientFormats(string text, string expected)
    {
        Assert.Equal(Version.Parse(expected), GitHubUpdateService.ExtractVersion(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Erstes Release")]
    [InlineData("v2")]
    public void ExtractVersion_ReturnsNullWhenNoVersionPresent(string? text)
    {
        Assert.Null(GitHubUpdateService.ExtractVersion(text));
    }

    // --- ParseReleases: shape as returned by GitHub's /releases endpoint (newest created first). ---

    private static string Release(string tagName, string? name = null, string assets = DefaultAssets,
        bool prerelease = false, bool draft = false, string body = "Neu: Auto-Update") => $$"""
        {
          "tag_name": "{{tagName}}",
          "name": "{{name ?? "KerwaKasse " + tagName}}",
          "draft": {{(draft ? "true" : "false")}},
          "prerelease": {{(prerelease ? "true" : "false")}},
          "published_at": "2026-07-10T10:39:41Z",
          "html_url": "https://github.com/manuelroehrer/KerwaKasse/releases/tag/{{tagName}}",
          "body": "{{body}}",
          "assets": {{assets}}
        }
        """;

    private static string Releases(params string[] releases) => "[" + string.Join(",", releases) + "]";

    private const string DefaultAssets = """
        [
          { "name": "notes.txt", "size": 10, "browser_download_url": "https://example.org/notes.txt" },
          { "name": "KerwaKasse_Setup.exe", "size": 63003605, "browser_download_url": "https://example.org/KerwaKasse_Setup.exe" }
        ]
        """;

    [Fact]
    public void ParseReleases_NewerRelease_ReportsUpdateWithInstaller()
    {
        var result = GitHubUpdateService.ParseReleases(
            Releases(Release("v2.10.0"), Release("v2.9.0")), new Version(2, 9, 0, 0));

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.Update);
        Assert.Equal(new Version(2, 10, 0), result.Update.Version);
        Assert.Equal("https://example.org/KerwaKasse_Setup.exe", result.Update.InstallerUrl);
        Assert.Equal(63003605, result.Update.InstallerSize);
        var note = Assert.Single(result.Update.ReleaseNotes);
        Assert.Equal(new Version(2, 10, 0), note.Version);
        Assert.Equal("Neu: Auto-Update", note.Text);
        Assert.Equal(new DateTimeOffset(2026, 7, 10, 10, 39, 41, TimeSpan.Zero), note.PublishedAt);
    }

    [Fact]
    public void ParseReleases_SeveralNewerReleases_ListsAllNewestFirst_InstallerFromNewest()
    {
        const string oldAssets = """[ { "name": "Setup_old.exe", "size": 1, "browser_download_url": "https://example.org/old.exe" } ]""";
        var result = GitHubUpdateService.ParseReleases(
            Releases(
                Release("v2.11.0", body: "elf"),
                Release("v2.10.0", body: "zehn", assets: oldAssets),
                Release("v2.9.0", body: "neun", assets: oldAssets),
                Release("v2.8.0", body: "acht", assets: oldAssets)),
            new Version(2, 8, 0, 0));

        Assert.Equal(new Version(2, 11, 0), result.Update!.Version);
        Assert.Equal("https://example.org/KerwaKasse_Setup.exe", result.Update.InstallerUrl);
        Assert.Equal("https://github.com/manuelroehrer/KerwaKasse/releases/tag/v2.11.0", result.Update.ReleaseUrl);
        Assert.Equal(new[] { "elf", "zehn", "neun" }, result.Update.ReleaseNotes.Select(n => n.Text));
    }

    [Fact]
    public void ParseReleases_OrdersByVersion_NotByCreationDate()
    {
        // A hotfix for an older line created after the newest release comes first in GitHub's list.
        var result = GitHubUpdateService.ParseReleases(
            Releases(Release("v2.9.1"), Release("v2.10.0"), Release("v2.9.0")), new Version(2, 9, 0));

        Assert.Equal(new Version(2, 10, 0), result.Update!.Version);
        Assert.Equal(new[] { new Version(2, 10, 0), new Version(2, 9, 1) }, result.Update.ReleaseNotes.Select(n => n.Version));
    }

    [Fact]
    public void ParseReleases_IgnoresPreReleasesAndDrafts()
    {
        var result = GitHubUpdateService.ParseReleases(
            Releases(Release("v2.12.0", draft: true), Release("v2.11.0-beta.1", prerelease: true), Release("v2.10.0")),
            new Version(2, 9, 0));

        Assert.Equal(new Version(2, 10, 0), result.Update!.Version);
        Assert.Single(result.Update.ReleaseNotes);
    }

    [Fact]
    public void ParseReleases_OnlyPreReleaseNewer_IsUpToDate()
    {
        var result = GitHubUpdateService.ParseReleases(
            Releases(Release("v2.10.0", prerelease: true), Release("v2.9.0")), new Version(2, 9, 0));

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public void ParseReleases_SameVersion_IsUpToDate_DespiteFourPartAssemblyVersion()
    {
        // Assembly versions carry four parts (2.9.0.0), tags three (v2.9.0); that must count as equal.
        var result = GitHubUpdateService.ParseReleases(
            Releases(Release("v2.9.0"), Release("v2.8.0")), new Version(2, 9, 0, 0));

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.Update);
    }

    [Fact]
    public void ParseReleases_OnlyOlderReleases_IsUpToDate()
    {
        var result = GitHubUpdateService.ParseReleases(Releases(Release("v2.8.0")), new Version(2, 9, 0, 0));

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public void ParseReleases_UnusableTag_FallsBackToReleaseName()
    {
        var result = GitHubUpdateService.ParseReleases(
            Releases(Release("latest", name: "KerwaKasse v2.10.0")), new Version(2, 9, 0, 0));

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new Version(2, 10, 0), result.Update!.Version);
    }

    [Fact]
    public void ParseReleases_ReleaseWithoutVersion_IsSkipped()
    {
        var result = GitHubUpdateService.ParseReleases(
            Releases(Release("latest", name: "neueste Version"), Release("v2.10.0")), new Version(2, 9, 0));

        Assert.Equal(new Version(2, 10, 0), result.Update!.Version);
        Assert.Single(result.Update.ReleaseNotes);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[ { "tag_name": "latest", "name": "neueste Version" } ]""")]
    [InlineData("""{ "message": "Not Found" }""")]
    public void ParseReleases_NoVersionAnywhere_Fails(string json)
    {
        var result = GitHubUpdateService.ParseReleases(json, new Version(2, 9, 0));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public void ParseReleases_PrefersSetupNamedExeOverOtherExes()
    {
        const string assets = """
            [
              { "name": "tool.exe", "size": 1, "browser_download_url": "https://example.org/tool.exe" },
              { "name": "KerwaKasse_Setup_v2.10.0.exe", "size": 2, "browser_download_url": "https://example.org/setup.exe" }
            ]
            """;

        var result = GitHubUpdateService.ParseReleases(Releases(Release("v2.10.0", assets: assets)), new Version(2, 9, 0));

        Assert.Equal("https://example.org/setup.exe", result.Update!.InstallerUrl);
    }

    [Fact]
    public void ParseReleases_NoExeAsset_ReportsUpdateWithoutInstaller()
    {
        // The update note must still appear; the UI then links to the release page instead.
        var result = GitHubUpdateService.ParseReleases(Releases(Release("v2.10.0", assets: "[]")), new Version(2, 9, 0));

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Null(result.Update!.InstallerUrl);
        Assert.Equal("https://github.com/manuelroehrer/KerwaKasse/releases/tag/v2.10.0", result.Update.ReleaseUrl);
    }
}
