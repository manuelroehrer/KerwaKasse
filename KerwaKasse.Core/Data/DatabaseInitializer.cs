using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KerwaKasse.Core.Data;

public static class DatabaseInitializer
{
    // Same LightGray used as the "no colour chosen" fallback throughout the app (ColorBorderHelper,
    // ProductModel); stored in the 8-digit ARGB form the colour picker itself writes.
    public const string DefaultProductColor = "#FFD3D3D3";

    /// <summary>The one entry point for opening a database file: keeps it a single file (journal mode) and
    /// brings its schema up to date (see <see cref="DatabaseMigrator"/>). Pass
    /// <paramref name="backupDirectory"/> to keep a copy of an existing file before it is migrated.</summary>
    /// <exception cref="DatabaseTooNewException">The file comes from a newer app version; it is left untouched.</exception>
    /// <exception cref="DatabaseMigrationException">A migration step failed and was rolled back.</exception>
    // The logger is optional so throwaway schema builds (e.g. the in-memory reference database in
    // DatabaseBackupService) and tests don't have to provide one.
    public static void Initialize(string connectionString, ILogger? logger = null, string? backupDirectory = null)
    {
        logger ??= NullLogger.Instance;

        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            // Checked before the journal mode, so a file of a newer version is not touched at all.
            DatabaseMigrator.ThrowIfTooNew(connection);
            EnsureDeleteJournalMode(connection, logger);
        }

        DatabaseMigrator.Migrate(connectionString, logger, backupDirectory);

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
}
