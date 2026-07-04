using KerwaKasse.Core.Data;
using KerwaKasse.Core.Tests.Fixtures;
using Microsoft.Data.Sqlite;

namespace KerwaKasse.Core.Tests.Data;

public class DatabaseInitializerTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _connectionString;

    public DatabaseInitializerTests()
    {
        _connectionString = $"Data Source=InitTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();
    }

    [Fact]
    public void Initialize_CreatesAllTables()
    {
        DatabaseInitializer.Initialize(_connectionString);

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
        using var reader = cmd.ExecuteReader();

        var tables = new List<string>();
        while (reader.Read())
            tables.Add(reader.GetString(0));

        Assert.Contains("Products", tables);
        Assert.Contains("Orders", tables);
        Assert.Contains("OrderPositions", tables);
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        DatabaseInitializer.Initialize(_connectionString);
        DatabaseInitializer.Initialize(_connectionString); // should not throw

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Products'";
        var count = (long)cmd.ExecuteScalar()!;

        Assert.Equal(1, count);
    }

    [Fact]
    public void Initialize_SwitchesWalDatabaseToDeleteJournal()
    {
        // Journal modes only apply to file databases; in-memory ones always report "memory".
        var dir = Path.Combine(Path.GetTempPath(), $"KerwaKasseTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var fileConnectionString = $"Data Source={Path.Combine(dir, "wal.db")}";
        try
        {
            using (var setup = new SqliteConnection(fileConnectionString))
            {
                setup.Open();
                using var toWal = setup.CreateCommand();
                toWal.CommandText = "PRAGMA journal_mode=WAL;";
                toWal.ExecuteScalar();
            }
            SqliteConnection.ClearAllPools();

            DatabaseInitializer.Initialize(fileConnectionString);

            using var connection = new SqliteConnection(fileConnectionString);
            connection.Open();
            using var check = connection.CreateCommand();
            check.CommandText = "PRAGMA journal_mode;";
            Assert.Equal("delete", (string)check.ExecuteScalar()!);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort temp cleanup */ }
        }
    }

    [Fact]
    public void Initialize_BackfillsNullProductColor()
    {
        // Simulates a row imported from outside the app (e.g. the MySQL migration tool), which the
        // app itself never produces since it always assigns a colour on product creation.
        DatabaseInitializer.Initialize(_connectionString);
        using (var insert = _connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO Products (Name, PriceCents, Color) VALUES ('Kinderkrenfleisch', 100, NULL)";
            insert.ExecuteNonQuery();
        }

        DatabaseInitializer.Initialize(_connectionString);

        using var check = _connection.CreateCommand();
        check.CommandText = "SELECT Color FROM Products WHERE Name = 'Kinderkrenfleisch'";
        Assert.Equal(DatabaseInitializer.DefaultProductColor, (string)check.ExecuteScalar()!);
    }

    [Fact]
    public void Initialize_AddsMissingMenuSortOrderColumn()
    {
        // Simulate a database whose Menus table predates the SortOrder column.
        using (var setup = _connection.CreateCommand())
        {
            setup.CommandText = "CREATE TABLE Menus (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL);";
            setup.ExecuteNonQuery();
        }

        DatabaseInitializer.Initialize(_connectionString);

        using var check = _connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Menus') WHERE name = 'SortOrder';";
        Assert.Equal(1L, (long)check.ExecuteScalar()!);
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
