using Dapper;
using KerwaKasse.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KerwaKasse.Core.Data;

public class DatabaseBackupService : IDatabaseBackupService
{
    private readonly string _connectionString;
    private readonly string _dbFilePath;
    private readonly ILogger<DatabaseBackupService> _logger;

    public DatabaseBackupService(string connectionString, string dbFilePath, ILogger<DatabaseBackupService> logger)
    {
        _connectionString = connectionString;
        _dbFilePath = dbFilePath;
        _logger = logger;
    }

    public DatabaseSummary GetSummary() => ReadSummary(_connectionString);

    /// <summary>Row counts of any KerwaKasse database with a current schema (not just the live one).</summary>
    public static DatabaseSummary ReadSummary(string connectionString)
    {
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        int Count(string table) => connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM {table}");
        return new DatabaseSummary(Count("Products"), Count("Orders"), Count("Menus"), Count("SavedAnalyses"));
    }

    public DatabaseFileCheck CheckDatabaseFile(string filePath)
    {
        if (!File.Exists(filePath)) return DatabaseFileCheck.Invalid;

        // Validate a private local copy, never the user's selected file directly. Opening a WAL-mode backup would
        // otherwise create -wal/-shm sidecars. It also may live on a network share, a UNC path or an on-demand location
        // like OneDrive etc., which might cause problems.
        // Copying to a local temp folder first prevents all of that:
        // any sidecars the read creates land in the temp folder and are
        // removed with it, and SQLite only ever works against a plain local file.
        string tempDir = Path.Combine(Path.GetTempPath(), "KerwaKasse_restore_" + Guid.NewGuid().ToString("N"));
        string candidatePath = Path.Combine(tempDir, "candidate.db");
        try
        {
            Directory.CreateDirectory(tempDir);
            File.Copy(filePath, candidatePath);

            // Pooling=False so the file handle is released on dispose and the temp folder can be deleted.
            using var connection = new SqliteConnection($"Data Source={candidatePath};Mode=ReadOnly;Pooling=False");
            connection.Open();
            var source = ReadSchema(connection);

            // Must look like a KerwaKasse database at all, not an arbitrary SQLite file: the two oldest,
            // always-present tables have to be there.
            if (!source.ContainsKey("Products") || !source.ContainsKey("Orders"))
            {
                _logger.LogWarning("File {FilePath} rejected: core tables (Products/Orders) missing", filePath);
                return DatabaseFileCheck.Invalid;
            }

            // A file of a newer app version would only be written in part by this one.
            int version = connection.ExecuteScalar<int>("PRAGMA user_version;");
            if (version > DatabaseMigrator.CurrentVersion)
            {
                _logger.LogWarning("File {FilePath} rejected: schema version {SchemaVersion} is newer than {SupportedVersion}",
                    filePath, version, DatabaseMigrator.CurrentVersion);
                return DatabaseFileCheck.NewerVersion;
            }

            // A file with a version was written by the app itself, and the migration after the restore takes
            // it from there. Files without one (2.11 and older) are checked against the baseline schema the
            // migration starts from: every table the file shares with it has to carry all its columns. Tables
            // the file lacks entirely are fine, the migration creates them. Extra columns are fine too, because
            // every query names its columns explicitly. A missing column, however, would only surface as a
            // crash after the irreversible restore, so reject such a file up front.
            if (version == 0)
            {
                foreach (var (table, expectedColumns) in GetBaselineSchema())
                {
                    if (source.TryGetValue(table, out var actualColumns) && !expectedColumns.IsSubsetOf(actualColumns))
                    {
                        _logger.LogWarning("File {FilePath} rejected: table {Table} is missing expected columns", filePath, table);
                        return DatabaseFileCheck.Invalid;
                    }
                }
            }

            return DatabaseFileCheck.Valid;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "File {FilePath} rejected: not a readable SQLite database", filePath);
            return DatabaseFileCheck.Invalid;
        }
        finally
        {
            try 
            { 
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); 
            }
            catch (Exception ex) 
            { 
                _logger.LogWarning(ex, "Could not remove temp validation folder {TempDir}", tempDir); 
            }
        }
    }

    /// <summary>Table name → column names of the baseline schema, built by the migration's own baseline step
    /// so the validation can never drift from it.</summary>
    private static Dictionary<string, HashSet<string>> GetBaselineSchema()
    {
        // A shared-cache in-memory database lives as long as one connection to it stays open; keep this one
        // open while the initializer (which opens and closes its own connection) builds the schema in it.
        string referenceConnectionString = $"Data Source=schema-reference-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        using var connection = new SqliteConnection(referenceConnectionString);
        connection.Open();
        DatabaseMigrator.Migrate(referenceConnectionString, NullLogger.Instance, targetVersion: DatabaseMigrator.BaselineVersion);
        return ReadSchema(connection);
    }

    /// <summary>Reads every user table of the open connection together with its column names.</summary>
    private static Dictionary<string, HashSet<string>> ReadSchema(SqliteConnection connection)
    {
        var schema = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var tables = connection.Query<string>(
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'");
        foreach (var table in tables)
        {
            var columns = connection.Query<string>("SELECT name FROM pragma_table_info(@table)", new { table })
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            schema[table] = columns;
        }
        return schema;
    }

    public void Restore(string sourceFilePath)
    {
        // The Metadata table describes the database itself, e.g. which event it is (see EventCatalog), not
        // the backed-up data. Keep the live database's entries, so restoring an old backup into
        // "Herbstkerwa" leaves the event named "Herbstkerwa".
        var metadata = ReadMetadata();

        // Release every pooled connection first, otherwise a retained file handle blocks the overwrite.
        SqliteConnection.ClearAllPools();

        File.Copy(sourceFilePath, _dbFilePath, overwrite: true);

        // Drop journal/WAL sidecars belonging to the previous database; applied to the new file they
        // would corrupt it. (Default journal mode leaves none, but a crash could.)
        foreach (var sidecar in new[] { "-wal", "-shm", "-journal" })
        {
            string path = _dbFilePath + sidecar;
            if (File.Exists(path)) File.Delete(path);
        }

        // The restored file may come from an older app version: bring it up to date. No extra copy is
        // needed, the chosen backup itself stays as it is.
        DatabaseInitializer.Initialize(_connectionString, _logger);
        WriteMetadata(metadata);

        _logger.LogInformation("Database restored from {SourceFilePath}", sourceFilePath);
    }

    private List<MetadataRow> ReadMetadata()
    {
        using var connection = new SqliteConnection(_connectionString);
        bool hasTable = connection.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Metadata'") > 0;
        return hasTable
            ? connection.Query<MetadataRow>("SELECT Key, Value FROM Metadata").ToList()
            : new List<MetadataRow>();
    }

    private void WriteMetadata(List<MetadataRow> rows)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM Metadata", transaction: transaction);
        connection.Execute("INSERT INTO Metadata (Key, Value) VALUES (@Key, @Value)", rows, transaction);
        transaction.Commit();
    }

    private sealed class MetadataRow
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
    }
}
