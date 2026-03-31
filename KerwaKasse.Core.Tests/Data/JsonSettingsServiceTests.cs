using KerwaKasse.Core.Data;

namespace KerwaKasse.Core.Tests.Data;

public class JsonSettingsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public JsonSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"KerwaKasseTest_{Guid.NewGuid():N}");
        _filePath = Path.Combine(_tempDir, "settings.json");
    }

    [Fact]
    public void Get_WithMissingFile_ReturnsDefault()
    {
        var sut = new JsonSettingsService(_filePath);

        Assert.Equal(42.0, sut.Get("missing", 42.0));
    }

    [Fact]
    public void Set_And_Get_RoundTrip()
    {
        var sut = new JsonSettingsService(_filePath);

        sut.Set("myKey", 123);
        Assert.Equal(123, sut.Get<int>("myKey"));
    }

    [Fact]
    public void Save_And_Reload_PersistsValues()
    {
        var sut = new JsonSettingsService(_filePath);
        sut.Set("name", "Bratwurst");
        sut.Set("price", 3.50);
        sut.Save();

        // New instance reads from disk
        var sut2 = new JsonSettingsService(_filePath);
        Assert.Equal("Bratwurst", sut2.Get<string>("name"));
        Assert.Equal(3.50, sut2.Get<double>("price"));
    }

    [Fact]
    public void Save_CreatesDirectoryIfMissing()
    {
        var sut = new JsonSettingsService(_filePath);
        sut.Set("key", "value");
        sut.Save();

        Assert.True(Directory.Exists(_tempDir));
        Assert.True(File.Exists(_filePath));
    }

    [Fact]
    public void Get_WithWrongType_ReturnsDefault()
    {
        var sut = new JsonSettingsService(_filePath);
        sut.Set("key", "not a number");

        // Trying to read as int should return default
        Assert.Equal(0, sut.Get<int>("key"));
    }

    [Fact]
    public void Get_NonexistentKey_ReturnsDefault()
    {
        var sut = new JsonSettingsService(_filePath);

        Assert.Null(sut.Get<string>("nope"));
        Assert.Equal(0, sut.Get<int>("nope"));
        Assert.Equal(99.0, sut.Get("nope", 99.0));
    }

    [Fact]
    public void Constructor_WithCorruptedJsonFile_DoesNotThrow()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_filePath, "{ this is not valid json !!!");

        var sut = new JsonSettingsService(_filePath);

        // Should fall back to empty settings, not crash
        Assert.Equal(42, sut.Get("key", 42));
    }

    [Fact]
    public void Constructor_WithEmptyFile_DoesNotThrow()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(_filePath, "");

        var sut = new JsonSettingsService(_filePath);

        Assert.Equal("default", sut.Get("key", "default"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }
}
