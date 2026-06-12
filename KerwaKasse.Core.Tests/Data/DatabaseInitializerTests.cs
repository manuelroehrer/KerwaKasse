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
