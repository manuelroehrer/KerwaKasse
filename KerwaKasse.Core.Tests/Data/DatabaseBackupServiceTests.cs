using KerwaKasse.Core.Data;
using KerwaKasse.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace KerwaKasse.Core.Tests.Data;

public class DatabaseBackupServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;
    private readonly string _connectionString;
    private readonly DatabaseBackupService _sut;

    public DatabaseBackupServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "KerwaKasseTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "kerwakasse.db");
        _connectionString = $"Data Source={_dbPath}";
        DatabaseInitializer.Initialize(_connectionString);
        _sut = new DatabaseBackupService(_connectionString, _dbPath, NullLogger<DatabaseBackupService>.Instance);
    }

    [Fact]
    public void GetSummary_CountsRows()
    {
        var products = new SqliteProductService(_connectionString, NullLogger<SqliteProductService>.Instance);
        products.Add(new Product { Name = "Bratwurst", Price = 3m });
        products.Add(new Product { Name = "Pommes", Price = 2m });

        var summary = _sut.GetSummary();

        Assert.Equal(2, summary.Products);
        Assert.Equal(0, summary.Orders);
        Assert.False(summary.IsEmpty);
    }

    [Fact]
    public void GetSummary_FreshDatabase_IsEmpty()
    {
        Assert.True(_sut.GetSummary().IsEmpty);
    }

    [Fact]
    public void IsValidDatabaseFile_TrueForKerwaKasseDatabase()
    {
        Assert.True(_sut.IsValidDatabaseFile(_dbPath));
    }

    [Fact]
    public void IsValidDatabaseFile_FalseForNonDatabaseFile()
    {
        var junk = Path.Combine(_dir, "junk.db");
        File.WriteAllText(junk, "this is not a database");

        Assert.False(_sut.IsValidDatabaseFile(junk));
    }

    [Fact]
    public void IsValidDatabaseFile_FalseForMissingFile()
    {
        Assert.False(_sut.IsValidDatabaseFile(Path.Combine(_dir, "does-not-exist.db")));
    }

    [Fact]
    public void IsValidDatabaseFile_TrueWhenOnlyOlderTablesPresent()
    {
        // A database from before the Menus/SavedAnalyses features: only the core tables, but those are
        // structurally current. The restore migration adds the missing tables, so the file is acceptable.
        var oldPath = CreateDatabase("old.db", """
            CREATE TABLE Products (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, PriceCents INTEGER NOT NULL, Available INTEGER NOT NULL DEFAULT 1, Color TEXT, SortOrder INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE Orders (Id INTEGER PRIMARY KEY AUTOINCREMENT, OrderTime TEXT NOT NULL);
            CREATE TABLE OrderPositions (OrderId INTEGER NOT NULL, ProductId INTEGER NOT NULL, Amount INTEGER NOT NULL, UnitPriceCents INTEGER NOT NULL, PRIMARY KEY (OrderId, ProductId));
            """);

        Assert.True(_sut.IsValidDatabaseFile(oldPath));
    }

    [Fact]
    public void IsValidDatabaseFile_FalseWhenSharedTableMissingColumn()
    {
        // Products exists but lacks the Color column the app reads. The migration can't add a column to an
        // existing table, so this file must be rejected before it overwrites the live database.
        var brokenPath = CreateDatabase("broken.db", """
            CREATE TABLE Products (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, PriceCents INTEGER NOT NULL);
            CREATE TABLE Orders (Id INTEGER PRIMARY KEY AUTOINCREMENT, OrderTime TEXT NOT NULL);
            """);

        Assert.False(_sut.IsValidDatabaseFile(brokenPath));
    }

    [Fact]
    public void IsValidDatabaseFile_TrueWhenSourceHasExtraColumn()
    {
        // An unknown extra column does no harm — every query names its columns explicitly — so a current
        // database with one added stays valid.
        var extraConnectionString = $"Data Source={Path.Combine(_dir, "extra.db")}";
        DatabaseInitializer.Initialize(extraConnectionString);
        ExecuteSql(extraConnectionString, "ALTER TABLE Products ADD COLUMN Notes TEXT;");
        SqliteConnection.ClearAllPools();

        Assert.True(_sut.IsValidDatabaseFile(Path.Combine(_dir, "extra.db")));
    }

    [Fact]
    public void IsValidDatabaseFile_WalModeBackup_IsValidAndLeavesNoSidecars()
    {
        // A backup taken while the database was in WAL mode: journal_mode=wal is persisted in the file
        // header. Validation must accept it without leaving -wal/-shm files next to the user's backup.
        var walPath = Path.Combine(_dir, "wal_backup.db");
        var walConnectionString = $"Data Source={walPath}";
        DatabaseInitializer.Initialize(walConnectionString); // current schema (and DELETE journal)
        ExecuteSql(walConnectionString, "PRAGMA journal_mode=WAL; PRAGMA wal_checkpoint(TRUNCATE);");
        SqliteConnection.ClearAllPools();
        // A real single-file backup carries no companion sidecars; drop any this setup produced.
        foreach (var sidecar in new[] { "-wal", "-shm", "-journal" })
            if (File.Exists(walPath + sidecar)) File.Delete(walPath + sidecar);

        bool valid = _sut.IsValidDatabaseFile(walPath);

        Assert.True(valid);
        Assert.False(File.Exists(walPath + "-wal"), "-wal sidecar was left next to the backup file");
        Assert.False(File.Exists(walPath + "-shm"), "-shm sidecar was left next to the backup file");
    }

    // Creates a database file with exactly the given schema (no initializer), then releases the file handle
    // so it can be opened independently as a restore source.
    private string CreateDatabase(string fileName, string schemaSql)
    {
        var path = Path.Combine(_dir, fileName);
        ExecuteSql($"Data Source={path}", schemaSql);
        SqliteConnection.ClearAllPools();
        return path;
    }

    private static void ExecuteSql(string connectionString, string sql)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Restore_ReplacesLiveDatabaseWithSourceContents()
    {
        new SqliteProductService(_connectionString, NullLogger<SqliteProductService>.Instance).Add(new Product { Name = "Original", Price = 1m });

        // A separate source database holding a different product.
        var sourcePath = Path.Combine(_dir, "source.db");
        var sourceConnectionString = $"Data Source={sourcePath}";
        DatabaseInitializer.Initialize(sourceConnectionString);
        new SqliteProductService(sourceConnectionString, NullLogger<SqliteProductService>.Instance).Add(new Product { Name = "Aus Backup", Price = 2m });
        SqliteConnection.ClearAllPools();

        _sut.Restore(sourcePath);

        var product = Assert.Single(new SqliteProductService(_connectionString, NullLogger<SqliteProductService>.Instance).GetAll());
        Assert.Equal("Aus Backup", product.Name);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }
}
