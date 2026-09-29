using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KerwaKasse.Core.Data;

public static class DatabaseInitializer
{
    // Same LightGray used as the "no colour chosen" fallback throughout the app (ColorBorderHelper,
    // ProductModel); stored in the 8-digit ARGB form the colour picker itself writes.
    public const string DefaultProductColor = "#FFD3D3D3";

    // The logger is optional so throwaway schema builds (e.g. the in-memory reference database in
    // DatabaseBackupService) and tests don't have to provide one.
    public static void Initialize(string connectionString, ILogger? logger = null)
    {
        logger ??= NullLogger.Instance;

        using var connection = new SqliteConnection(connectionString);
        connection.Open();

        EnsureDeleteJournalMode(connection, logger);

        // If you add or remove user-data tables here, also update:
        //   • DatabaseSummary (IDatabaseBackupService.cs)  — the record listing the counted tables
        //   • DatabaseBackupService.ReadSummary()          — the queries that produce those counts
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

            -- Facts about the database itself rather than user data (e.g. the event name, see
            -- EventCatalog), so it is deliberately not counted in DatabaseSummary.
            CREATE TABLE IF NOT EXISTS Metadata (
                Key TEXT PRIMARY KEY,
                Value TEXT NOT NULL
            );
            """;
        command.ExecuteNonQuery();

        EnsureMenuSortOrderColumn(connection, logger);
        EnsureProductColorAssigned(connection, logger);

        logger.LogDebug("Database schema verified/created");
    }

    /// <summary>The database must stay a single self-contained file: backups are plain file copies
    /// and a restore overwrites the file. In WAL mode recent commits can live in a -wal sidecar
    /// (a file copy would silently miss them, and stray -wal/-shm files linger next to the
    /// database), so switch such databases back to the classic rollback journal. The journal mode
    /// is persisted in the database file, so the change applies once per affected database;
    /// in-memory databases report their own "memory" mode and are left alone.</summary>
    private static void EnsureDeleteJournalMode(SqliteConnection connection, ILogger logger)
    {
        using var query = connection.CreateCommand();
        query.CommandText = "PRAGMA journal_mode;";
        var currentMode = (string)query.ExecuteScalar()!;
        if (currentMode is "delete" or "memory") return;

        using var change = connection.CreateCommand();
        change.CommandText = "PRAGMA journal_mode=DELETE;";
        change.ExecuteScalar();

        logger.LogInformation("Journal mode changed from {PreviousMode} to delete", currentMode);
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

    /// <summary>The app itself always assigns a colour on product creation (see
    /// ProductsViewModel.BeginAddProduct), so a NULL Color can only come from data imported outside
    /// the app (e.g. the MySQL migration tool, which allowed it). Backfill such rows with the
    /// default colour so every view can rely on Products.Color always being set.</summary>
    private static void EnsureProductColorAssigned(SqliteConnection connection, ILogger logger)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Products SET Color = @default WHERE Color IS NULL";
        command.Parameters.AddWithValue("@default", DefaultProductColor);
        int affected = command.ExecuteNonQuery();

        if (affected > 0)
            logger.LogInformation("Migration applied: {ProductCount} product(s) without a colour assigned the default colour", affected);
    }
}
