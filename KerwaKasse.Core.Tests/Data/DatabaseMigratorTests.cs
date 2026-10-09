using Dapper;
using KerwaKasse.Core.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace KerwaKasse.Core.Tests.Data;

public class DatabaseMigratorTests : IDisposable
{
    private readonly string _dir;
    private readonly string _backupDir;
    private readonly string _connectionString;

    public DatabaseMigratorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "KerwaKasseMigratorTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _backupDir = Path.Combine(_dir, "backups");
        // No pooling, so no connection keeps the file open after a test.
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(_dir, "kerwa.db"), Pooling = false }.ToString();
    }

    private void Migrate(string? backupDirectory = null) =>
        DatabaseMigrator.Migrate(_connectionString, NullLogger.Instance, backupDirectory);

    // A database as release 2.11 left it: no schema version, one product without a colour and a Menus
    // table from before its SortOrder column.
    private void CreateUnversionedDatabase() => Execute("""
        CREATE TABLE Products (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, PriceCents INTEGER NOT NULL, Available INTEGER NOT NULL DEFAULT 1, Color TEXT, SortOrder INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE Orders (Id INTEGER PRIMARY KEY AUTOINCREMENT, OrderTime TEXT NOT NULL);
        CREATE TABLE OrderPositions (OrderId INTEGER NOT NULL, ProductId INTEGER NOT NULL, Amount INTEGER NOT NULL, UnitPriceCents INTEGER NOT NULL, PRIMARY KEY (OrderId, ProductId));
        CREATE TABLE Menus (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL);
        INSERT INTO Products (Name, PriceCents, Color) VALUES ('Bratwurst', 300, NULL);
        """);

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Execute(sql);
    }

    private T Scalar<T>(string sql)
    {
        using var connection = new SqliteConnection(_connectionString);
        return connection.ExecuteScalar<T>(sql)!;
    }

    private int SchemaVersion => Scalar<int>("PRAGMA user_version;");

    private bool TableExists(string name) =>
        Scalar<long>($"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{name}';") == 1;

    [Fact]
    public void Migrate_NewFile_RunsAllStepsWithoutABackup()
    {
        Migrate(_backupDir);

        Assert.Equal(DatabaseMigrator.CurrentVersion, SchemaVersion);
        Assert.True(TableExists("Products"));
        Assert.False(Directory.Exists(_backupDir)); // nothing worth keeping yet
    }

    [Fact]
    public void Migrate_UnversionedFile_KeepsItsDataAndRepairsKnownGaps()
    {
        CreateUnversionedDatabase();

        Migrate();

        Assert.Equal(DatabaseMigrator.CurrentVersion, SchemaVersion);
        Assert.Equal("Bratwurst", Scalar<string>("SELECT Name FROM Products;"));
        Assert.Equal(DatabaseInitializer.DefaultProductColor, Scalar<string>("SELECT Color FROM Products;"));
        Assert.Equal(1L, Scalar<long>("SELECT COUNT(*) FROM pragma_table_info('Menus') WHERE name = 'SortOrder';"));
        Assert.True(TableExists("Metadata"));
    }

    [Fact]
    public void Migrate_ExistingFile_IsCopiedOnceBeforeItChanges()
    {
        CreateUnversionedDatabase();

        Migrate(_backupDir);

        var backup = Assert.Single(Directory.GetFiles(_backupDir));
        Assert.StartsWith("kerwa.schema0.", Path.GetFileName(backup));
        using (var copy = new SqliteConnection($"Data Source={backup};Pooling=False"))
        {
            Assert.Equal(0, copy.ExecuteScalar<int>("PRAGMA user_version;")); // the state before the migration
            Assert.Equal("Bratwurst", copy.ExecuteScalar<string>("SELECT Name FROM Products;"));
        }

        // Already up to date: no further copy.
        Migrate(_backupDir);
        Assert.Single(Directory.GetFiles(_backupDir));
    }

    [Fact]
    public void Migrate_FileOfANewerVersion_IsRefusedAndLeftUntouched()
    {
        Migrate();
        int newer = DatabaseMigrator.CurrentVersion + 1;
        Execute($"PRAGMA user_version = {newer};");

        var ex = Assert.Throws<DatabaseTooNewException>(() => Migrate(_backupDir));

        Assert.Equal(newer, ex.FileVersion);
        Assert.Equal(DatabaseMigrator.CurrentVersion, ex.SupportedVersion);
        Assert.Equal(newer, SchemaVersion);
        Assert.False(Directory.Exists(_backupDir));
    }

    [Fact]
    public void Migrate_FailingStep_IsRolledBackAndTheFileKeepsThePreviousVersion()
    {
        var steps = new List<MigrationStep>
        {
            new(1, "first", (c, t, _) => c.Execute("CREATE TABLE First (Id INTEGER);", transaction: t)),
            new(2, "second", (c, t, _) =>
            {
                c.Execute("CREATE TABLE Second (Id INTEGER);", transaction: t);
                throw new InvalidOperationException("step failed");
            })
        };

        var ex = Assert.Throws<DatabaseMigrationException>(() =>
            DatabaseMigrator.Migrate(_connectionString, NullLogger.Instance, null, null, steps));

        Assert.Equal(2, ex.FailedVersion);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal(1, SchemaVersion);
        Assert.True(TableExists("First"));
        Assert.False(TableExists("Second"));
    }

    [Fact]
    public void Migrate_TargetVersion_StopsAtThatStep()
    {
        var steps = new List<MigrationStep>
        {
            new(1, "first", (c, t, _) => c.Execute("CREATE TABLE First (Id INTEGER);", transaction: t)),
            new(2, "second", (c, t, _) => c.Execute("CREATE TABLE Second (Id INTEGER);", transaction: t))
        };

        DatabaseMigrator.Migrate(_connectionString, NullLogger.Instance, null, 1, steps);

        Assert.Equal(1, SchemaVersion);
        Assert.False(TableExists("Second"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort temp cleanup */ }
    }
}
