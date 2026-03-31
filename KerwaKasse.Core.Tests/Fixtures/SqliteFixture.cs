using KerwaKasse.Core.Data;
using Microsoft.Data.Sqlite;

namespace KerwaKasse.Core.Tests.Fixtures;

/// <summary>
/// Provides an in-memory SQLite database for integration tests.
/// The connection stays open for the lifetime of the fixture so the
/// in-memory database persists across all queries within a single test.
/// </summary>
public class SqliteFixture : IDisposable
{
    public SqliteConnection Connection { get; }
    public string ConnectionString { get; }

    public SqliteFixture()
    {
        // Shared cache allows multiple connections to access the same in-memory DB
        ConnectionString = "Data Source=InMemoryTestDb;Mode=Memory;Cache=Shared";
        Connection = new SqliteConnection(ConnectionString);
        Connection.Open();

        DatabaseInitializer.Initialize(ConnectionString);
    }

    public void Dispose()
    {
        Connection.Dispose();
    }
}
