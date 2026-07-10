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

    // --- ParseLatestRelease: shape as returned by GitHub's /releases/latest endpoint. ---

    private static string ReleaseJson(string tagName = "v2.10.0", string name = "KerwaKasse v2.10.0", string assets = DefaultAssets) => $$"""
        {
          "tag_name": "{{tagName}}",
          "name": "{{name}}",
          "html_url": "https://github.com/manuelroehrer/KerwaKasse/releases/tag/{{tagName}}",
          "body": "Neu: Auto-Update",
          "assets": {{assets}}
        }
        """;

    private const string DefaultAssets = """
        [
          { "name": "notes.txt", "size": 10, "browser_download_url": "https://example.org/notes.txt" },
          { "name": "KerwaKasse_Setup_v2.10.0.exe", "size": 63003605, "browser_download_url": "https://example.org/KerwaKasse_Setup_v2.10.0.exe" }
        ]
        """;

    [Fact]
    public void ParseLatestRelease_NewerRelease_ReportsUpdateWithInstaller()
    {
        var result = GitHubUpdateService.ParseLatestRelease(ReleaseJson(), new Version(2, 9, 0, 0));

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.NotNull(result.Update);
        Assert.Equal(new Version(2, 10, 0), result.Update.Version);
        Assert.Equal("https://example.org/KerwaKasse_Setup_v2.10.0.exe", result.Update.InstallerUrl);
        Assert.Equal(63003605, result.Update.InstallerSize);
        Assert.Equal("Neu: Auto-Update", result.Update.ReleaseNotes);
    }

    [Fact]
    public void ParseLatestRelease_SameVersion_IsUpToDate_DespiteFourPartAssemblyVersion()
    {
        // Assembly versions carry four parts (2.9.0.0), tags three (v2.9.0); that must count as equal.
        var result = GitHubUpdateService.ParseLatestRelease(
            ReleaseJson(tagName: "v2.9.0", name: "KerwaKasse v2.9.0"), new Version(2, 9, 0, 0));

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
        Assert.Null(result.Update);
    }

    [Fact]
    public void ParseLatestRelease_OlderRelease_IsUpToDate()
    {
        var result = GitHubUpdateService.ParseLatestRelease(
            ReleaseJson(tagName: "v2.8.0"), new Version(2, 9, 0, 0));

        Assert.Equal(UpdateCheckStatus.UpToDate, result.Status);
    }

    [Fact]
    public void ParseLatestRelease_UnusableTag_FallsBackToReleaseName()
    {
        var result = GitHubUpdateService.ParseLatestRelease(
            ReleaseJson(tagName: "latest", name: "KerwaKasse v2.10.0"), new Version(2, 9, 0, 0));

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Equal(new Version(2, 10, 0), result.Update!.Version);
    }

    [Fact]
    public void ParseLatestRelease_NoVersionAnywhere_Fails()
    {
        var result = GitHubUpdateService.ParseLatestRelease(
            ReleaseJson(tagName: "latest", name: "neueste Version"), new Version(2, 9, 0, 0));

        Assert.Equal(UpdateCheckStatus.Failed, result.Status);
    }

    [Fact]
    public void ParseLatestRelease_PrefersSetupNamedExeOverOtherExes()
    {
        const string assets = """
            [
              { "name": "tool.exe", "size": 1, "browser_download_url": "https://example.org/tool.exe" },
              { "name": "KerwaKasse_Setup_v2.10.0.exe", "size": 2, "browser_download_url": "https://example.org/setup.exe" }
            ]
            """;

        var result = GitHubUpdateService.ParseLatestRelease(ReleaseJson(assets: assets), new Version(2, 9, 0));

        Assert.Equal("https://example.org/setup.exe", result.Update!.InstallerUrl);
    }

    [Fact]
    public void ParseLatestRelease_NoExeAsset_ReportsUpdateWithoutInstaller()
    {
        // The update note must still appear; the UI then links to the release page instead.
        var result = GitHubUpdateService.ParseLatestRelease(ReleaseJson(assets: "[]"), new Version(2, 9, 0));

        Assert.Equal(UpdateCheckStatus.UpdateAvailable, result.Status);
        Assert.Null(result.Update!.InstallerUrl);
        Assert.Equal("https://github.com/manuelroehrer/KerwaKasse/releases/tag/v2.10.0", result.Update.ReleaseUrl);
    }
}
