using KerwaKasse.Core.Data;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace KerwaKasse.Core.Tests.Data;

public class EventCatalogTests : IDisposable
{
    private readonly string _dir;
    private readonly string _databaseDir;
    private readonly string _legacyPath;

    public EventCatalogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "KerwaKasseEventsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _databaseDir = Path.Combine(_dir, EventCatalog.DatabaseFolderName);
        _legacyPath = Path.Combine(_dir, EventCatalog.LegacyDatabaseFileName);
    }

    // A fresh settings instance re-reads settings.json, like the next app start does.
    private JsonSettingsService NewSettings() =>
        new(Path.Combine(_dir, "settings.json"), NullLogger<JsonSettingsService>.Instance);

    private EventCatalog NewCatalog() =>
        new(_dir, NewSettings(), NullLogger<EventCatalog>.Instance);

    private static string ConnectionString(string path) => $"Data Source={path}";

    // A kerwakasse.db as versions up to 2.11 left it. The pool is cleared afterwards: a pooled connection
    // would keep the file open, so the migration could not move it (and would fall back to using it in place).
    private void CreateLegacyDatabase()
    {
        DatabaseInitializer.Initialize(ConnectionString(_legacyPath));
        SqliteConnection.ClearAllPools();
    }

    private static void AddProduct(string dbPath, string name)
    {
        new SqliteProductService(ConnectionString(dbPath), NullLogger<SqliteProductService>.Instance)
            .Add(new Product { Name = name, Price = 3m });
        SqliteConnection.ClearAllPools();
    }

    // ── First start ──

    [Fact]
    public void FreshInstallation_StartsWithAnEmptyKerwaEvent()
    {
        var sut = NewCatalog();

        var single = Assert.Single(sut.GetAll());
        Assert.Equal(EventCatalog.DefaultEventName, single.Name);
        Assert.Equal(Path.Combine(_databaseDir, "kerwa.db"), sut.ActiveFilePath);
        Assert.Equal(0, single.Products);
        Assert.Null(sut.StartupFallback);
    }

    [Fact]
    public void LegacyDatabase_IsMovedIntoDatabaseFolderAndStaysActive()
    {
        CreateLegacyDatabase();
        AddProduct(_legacyPath, "Bratwurst");

        var sut = NewCatalog();

        Assert.False(File.Exists(_legacyPath));
        var migrated = Assert.Single(sut.GetAll());
        Assert.Equal(Path.Combine(_databaseDir, "kerwa.db"), migrated.FilePath);
        Assert.Equal(EventCatalog.DefaultEventName, migrated.Name);
        Assert.Equal(1, migrated.Products);
        Assert.Equal(migrated.FilePath, sut.ActiveFilePath);
        Assert.Equal(migrated.FilePath, NewCatalog().ActiveFilePath);
        Assert.Null(sut.AdoptedLegacyEventName);
    }

    [Fact]
    public void LegacyDatabase_ReappearingEmpty_IsRemoved()
    {
        // An older version started by mistake after the migration creates a new, empty kerwakasse.db.
        var kerwa = NewCatalog().ActiveFilePath;
        CreateLegacyDatabase();

        var sut = NewCatalog();

        Assert.False(File.Exists(_legacyPath));
        Assert.Single(sut.GetAll());
        Assert.Equal(kerwa, sut.ActiveFilePath);
        Assert.Null(sut.AdoptedLegacyEventName);
    }

    [Fact]
    public void LegacyDatabase_ReappearingWithData_IsAddedWithoutTakingOver()
    {
        // ... and if something was booked with that older version, it is kept as an additional event.
        var kerwa = NewCatalog().ActiveFilePath;
        CreateLegacyDatabase();
        AddProduct(_legacyPath, "Bratwurst");

        var sut = NewCatalog();

        Assert.False(File.Exists(_legacyPath));
        Assert.Equal(new[] { "Kerwa", "Kerwa (2)" }, sut.GetAll().Select(e => e.Name));
        Assert.Equal(kerwa, sut.ActiveFilePath);
        Assert.Equal("Kerwa (2)", sut.AdoptedLegacyEventName);
    }

    [Fact]
    public void LegacyDatabase_InUseByAnotherProgram_IsUsedInPlaceUntilItCanBeMoved()
    {
        CreateLegacyDatabase();

        // Opened without FileShare.Delete, like SQLite does in another process: the move must fail.
        using (new FileStream(_legacyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            var blocked = NewCatalog();
            Assert.Equal(_legacyPath, blocked.ActiveFilePath);
            Assert.Contains(blocked.GetAll(), e => e.FilePath == _legacyPath);
        }

        var later = NewCatalog();
        Assert.False(File.Exists(_legacyPath));
        Assert.Equal(Path.Combine(_databaseDir, "kerwa.db"), later.ActiveFilePath);
    }

    // ── Active event ──

    [Fact]
    public void SetActive_IsRememberedAcrossRestarts()
    {
        var created = NewCatalog().Create("Herbstkerwa");
        NewCatalog().SetActive(created.FilePath);

        var afterRestart = NewCatalog();

        Assert.Equal(created.FilePath, afterRestart.ActiveFilePath);
        Assert.Null(afterRestart.StartupFallback);
    }

    [Fact]
    public void SetActive_StoresOnlyTheFileName()
    {
        var created = NewCatalog().Create("Herbstkerwa");
        NewCatalog().SetActive(created.FilePath);

        Assert.Equal("herbstkerwa.db", NewSettings().Get<string>("activeEventDatabase"));
    }

    [Fact]
    public void ActiveFilePath_StoredFileMissing_FallsBackToAnotherEventAndReportsIt()
    {
        var sut = NewCatalog();
        var kerwa = sut.ActiveFilePath;
        var created = sut.Create("Herbstkerwa");
        sut.SetActive(created.FilePath);
        File.Delete(created.FilePath);

        var afterRestart = NewCatalog();

        Assert.Equal(kerwa, afterRestart.ActiveFilePath);
        Assert.Equal(new ActiveEventFallback("herbstkerwa.db", NewEventCreated: false), afterRestart.StartupFallback);
    }

    [Fact]
    public void ActiveFilePath_NoEventLeft_CreatesNewOneAndReportsIt()
    {
        var sut = NewCatalog();
        File.Delete(sut.ActiveFilePath);

        var afterRestart = NewCatalog();

        Assert.Equal(EventCatalog.DefaultEventName, Assert.Single(afterRestart.GetAll()).Name);
        Assert.Equal(new ActiveEventFallback("kerwa.db", NewEventCreated: true), afterRestart.StartupFallback);
    }

    [Fact]
    public void SetActive_FileOutsideCatalog_Throws()
    {
        var foreign = Path.Combine(_dir, "foreign.db");
        DatabaseInitializer.Initialize(ConnectionString(foreign));

        Assert.Throws<ArgumentException>(() => NewCatalog().SetActive(foreign));
    }

    // ── Create, import, rename, delete ──

    [Fact]
    public void Create_AddsEmptyEventInDatabaseFolder()
    {
        var sut = NewCatalog();

        var created = sut.Create("Herbstkerwa 2026");

        Assert.Equal(Path.Combine(_databaseDir, "herbstkerwa-2026.db"), created.FilePath);
        Assert.Equal("Herbstkerwa 2026", created.Name);
        Assert.Equal(0, created.Products);
        Assert.Equal(2, sut.GetAll().Count);
    }

    [Fact]
    public void Create_SameSlugTwice_GetsDistinctFiles()
    {
        var sut = NewCatalog();

        var first = sut.Create("Sommerfest");
        var second = sut.Create("Sommerfest!");

        Assert.NotEqual(first.FilePath, second.FilePath);
    }

    [Fact]
    public void Import_CopiesFileAndStoresName()
    {
        var backup = Path.Combine(_dir, "backup.db");
        DatabaseInitializer.Initialize(ConnectionString(backup));
        AddProduct(backup, "Bier");

        var sut = NewCatalog();
        var imported = sut.Import(backup, "Kerwa 2025");

        Assert.True(File.Exists(backup));
        Assert.Equal(_databaseDir, Path.GetDirectoryName(imported.FilePath));
        var listed = sut.GetAll().Single(e => e.FilePath == imported.FilePath);
        Assert.Equal("Kerwa 2025", listed.Name);
        Assert.Equal(1, listed.Products);
    }

    [Fact]
    public void Rename_ChangesNameButKeepsFile()
    {
        var sut = NewCatalog();
        var created = sut.Create("Herbstkerwa");

        sut.Rename(created.FilePath, "Herbstkerwa 2026");

        var renamed = sut.GetAll().Single(e => e.FilePath == created.FilePath);
        Assert.Equal("Herbstkerwa 2026", renamed.Name);
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        var sut = NewCatalog();
        var created = sut.Create("Herbstkerwa");

        sut.Delete(created.FilePath);

        Assert.False(File.Exists(created.FilePath));
        Assert.Single(sut.GetAll());
    }

    [Fact]
    public void Delete_ActiveEvent_Throws()
    {
        var sut = NewCatalog();

        Assert.Throws<InvalidOperationException>(() => sut.Delete(sut.ActiveFilePath));
        Assert.True(File.Exists(sut.ActiveFilePath));
    }

    [Fact]
    public void GetAll_ReportsOrderCountAndRange()
    {
        var sut = NewCatalog();
        var products = new SqliteProductService(ConnectionString(sut.ActiveFilePath), NullLogger<SqliteProductService>.Instance);
        products.Add(new Product { Name = "Bratwurst", Price = 3m });
        int productId = products.GetAll().Single().Id;
        var orders = new SqliteOrderService(ConnectionString(sut.ActiveFilePath), NullLogger<SqliteOrderService>.Instance);
        orders.PlaceOrder(new[] { new OrderPosition { ProductId = productId, Amount = 1, UnitPrice = 3m } });

        var single = sut.GetAll().Single();

        Assert.Equal(1, single.Products);
        Assert.Equal(1, single.Orders);
        Assert.Equal(DateTime.Today, single.FirstOrder?.Date);
    }

    [Theory]
    [InlineData("Herbstkerwa", "herbstkerwa")]
    [InlineData("Kärwa Bärnfels 2026", "kaerwa-baernfels-2026")]
    [InlineData("  Straßenfest / Café  ", "strassenfest-cafe")]
    [InlineData("!!!", "veranstaltung")]
    public void Slugify_ProducesFileSystemSafeNames(string name, string expected)
    {
        Assert.Equal(expected, EventCatalog.Slugify(name));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }
}
