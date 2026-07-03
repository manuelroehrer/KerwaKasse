using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KerwaKasse.Core.Data;

public static class DatabaseInitializer
{
    // The logger is optional so throwaway schema builds (e.g. the in-memory reference database in
    // DatabaseBackupService) and tests don't have to provide one.
    public static void Initialize(string connectionString, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        // If you add or remove user-data tables here, also update:
        //   • DatabaseSummary (IDatabaseBackupService.cs)  — the record listing the counted tables
        //   • DatabaseBackupService.GetSummary()           — the queries that produce those counts
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS Products (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                PriceCents INTEGER NOT NULL,
                Available INTEGER NOT NULL DEFAULT 1,
                Color TEXT,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrderTime TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS OrderPositions (
                OrderId INTEGER NOT NULL,
                ProductId INTEGER NOT NULL,
                Amount INTEGER NOT NULL,
                UnitPriceCents INTEGER NOT NULL,
                PRIMARY KEY (OrderId, ProductId),
                FOREIGN KEY (OrderId) REFERENCES Orders(Id),
                FOREIGN KEY (ProductId) REFERENCES Products(Id)
            );

            CREATE TABLE IF NOT EXISTS Menus (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS MenuProducts (
                MenuId INTEGER NOT NULL,
                ProductId INTEGER NOT NULL,
                PRIMARY KEY (MenuId, ProductId),
                FOREIGN KEY (MenuId) REFERENCES Menus(Id) ON DELETE CASCADE,
                FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS SavedAnalyses (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                AllProducts INTEGER NOT NULL DEFAULT 1,
                FromTime TEXT,
                ToTime TEXT,
                SortOrder INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS SavedAnalysisProducts (
                SavedAnalysisId INTEGER NOT NULL,
                ProductId INTEGER NOT NULL,
                PRIMARY KEY (SavedAnalysisId, ProductId),
                FOREIGN KEY (SavedAnalysisId) REFERENCES SavedAnalyses(Id) ON DELETE CASCADE,
                FOREIGN KEY (ProductId) REFERENCES Products(Id) ON DELETE CASCADE
            );
            """;
        command.ExecuteNonQuery();

        EnsureMenuSortOrderColumn(connection, logger);

        logger.LogDebug("Database schema verified/created");
    }

    /// <summary>Databases created before the Menus.SortOrder column existed don't get it from the
    /// CREATE TABLE above (that only runs for new tables); add it where it is still missing.</summary>
    private static void EnsureMenuSortOrderColumn(SqliteConnection connection, ILogger logger)
    {
        using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Menus') WHERE name = 'SortOrder';";
        if ((long)check.ExecuteScalar() > 0) return;

        using var alter = connection.CreateCommand();
        alter.CommandText = "ALTER TABLE Menus ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0;";
        alter.ExecuteNonQuery();

        logger.LogInformation("Migration applied: column SortOrder added to table Menus");
    }
}
