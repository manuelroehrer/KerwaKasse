using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KerwaKasse.Core.Data;

/// <summary>Brings a database file to the current schema in numbered steps. Every file records the step it
/// has reached in its own header (PRAGMA user_version; 0 for every file of version 2.11 and older), so a
/// file of any age runs exactly the steps it lacks, in order, each in one transaction together with its
/// version number. A new file runs all steps from 0 as well, so new and migrated databases end up identical.
/// <para>A schema change is a new step at the end; a released step is never changed afterwards. The schema
/// version is independent of the app version, since not every release changes the schema.</para>
/// <para>If a step adds or removes user-data tables, also update DatabaseSummary (IDatabaseBackupService.cs)
/// and DatabaseBackupService.ReadSummary(), which count them.</para></summary>
public static class DatabaseMigrator
{
    /// <summary>The version of the baseline step: the schema as of release 2.12, before the first real
    /// migration. Files without a version are checked against it before they are restored.</summary>
    public const int BaselineVersion = 1;

    private static readonly IReadOnlyList<MigrationStep> Steps =
    [
        new(BaselineVersion, "baseline schema of release 2.12", ApplyBaseline),
        new(2, "free entries and variable prices (release 2.12)", ApplyFreeEntriesAndVariablePrices),
    ];

    /// <summary>The schema version this app version writes.</summary>
    public static int CurrentVersion => Steps[^1].Version;

    /// <summary>Runs the missing steps up to <paramref name="targetVersion"/> (default: the current version).
    /// When <paramref name="backupDirectory"/> is given and the file already holds tables, a copy of it is
    /// put there before the first step changes anything.</summary>
    /// <exception cref="DatabaseTooNewException">The file comes from a newer app version; it is left untouched.</exception>
    /// <exception cref="DatabaseMigrationException">A step failed; it was rolled back completely.</exception>
    public static void Migrate(string connectionString, ILogger logger, string? backupDirectory = null, int? targetVersion = null) =>
        Migrate(connectionString, logger, backupDirectory, targetVersion, Steps);

    /// <summary>Throws <see cref="DatabaseTooNewException"/> when the file of <paramref name="connection"/>
    /// has a higher schema version than this app version knows.</summary>
    public static void ThrowIfTooNew(SqliteConnection connection)
    {
        int version = ReadVersion(connection);
        if (version > CurrentVersion)
            throw new DatabaseTooNewException(version, CurrentVersion);
    }

    internal static void Migrate(string connectionString, ILogger logger, string? backupDirectory, int? targetVersion,
        IReadOnlyList<MigrationStep> steps)
    {
        int newest = steps.Count > 0 ? steps[^1].Version : 0;

        // Foreign keys off, as SQLite recommends for schema changes: rebuilding a table copies its rows
        // into a new one, which must not fail on old rows the constraints would reject (e.g. from data
        // imported outside the app). The setting only applies to this connection.
        var builder = new SqliteConnectionStringBuilder(connectionString) { ForeignKeys = false };
        using var connection = new SqliteConnection(builder.ToString());
        connection.Open();

        int version = ReadVersion(connection);
        if (version > newest)
            throw new DatabaseTooNewException(version, newest);

        int target = targetVersion ?? newest;
        var pending = steps.Where(s => s.Version > version && s.Version <= target).ToList();
        if (pending.Count == 0) return;

        string? backupPath = null;
        if (backupDirectory != null && HasTables(connection))
        {
            try
            {
                backupPath = Backup(connection.DataSource, backupDirectory, version, logger);
            }
            catch (Exception ex)
            {
                // Without the copy nothing gets changed.
                throw new DatabaseMigrationException(pending[0].Version, null, ex);
            }
        }

        foreach (var step in pending)
        {
            try
            {
                using var transaction = connection.BeginTransaction();
                step.Apply(connection, transaction, logger);
                // Part of the same transaction: a file is never marked as migrated without the step's
                // changes, nor changed without being marked.
                connection.Execute($"PRAGMA user_version = {step.Version};", transaction: transaction);
                transaction.Commit();
            }
            catch (Exception ex)
            {
                throw new DatabaseMigrationException(step.Version, backupPath, ex);
            }

            logger.LogInformation("Migration to schema version {SchemaVersion} applied: {Description}", step.Version, step.Description);
        }
    }

    private static int ReadVersion(SqliteConnection connection) =>
        connection.ExecuteScalar<int>("PRAGMA user_version;");

    private static bool HasTables(SqliteConnection connection) =>
        connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%';") > 0;

    /// <summary>Copies the database file into <paramref name="backupDirectory"/>, named after the file, the
    /// version it had and the time ("kerwa.schema0.20261009-153012.db").</summary>
    private static string Backup(string filePath, string backupDirectory, int version, ILogger logger)
    {
        Directory.CreateDirectory(backupDirectory);
        string backupPath = Path.Combine(backupDirectory,
            $"{Path.GetFileNameWithoutExtension(filePath)}.schema{version}.{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(filePath)}");

        // No transaction is open and the journal mode is DELETE (see DatabaseInitializer), so the file
        // alone holds the complete database.
        File.Copy(filePath, backupPath);

        logger.LogInformation("Database {FilePath} backed up to {BackupPath} before migrating from schema version {SchemaVersion}",
            filePath, backupPath, version);
        return backupPath;
    }

    // ── Steps ──────────────────────────────────────────────

    /// <summary>Step 1: the schema as the app built it up to release 2.12, without a version. It runs on
    /// files of every older release, so it only creates what is missing and repairs the two known gaps.</summary>
    private static void ApplyBaseline(SqliteConnection connection, SqliteTransaction transaction, ILogger logger)
    {
        connection.Execute("""
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
            """, transaction: transaction);

        // Databases created before the Menus.SortOrder column existed don't get it from the CREATE TABLE
        // above (that only runs for new tables).
        if (connection.ExecuteScalar<long>("SELECT COUNT(*) FROM pragma_table_info('Menus') WHERE name = 'SortOrder';",
                transaction: transaction) == 0)
        {
            connection.Execute("ALTER TABLE Menus ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0;", transaction: transaction);
            logger.LogInformation("Migration applied: column SortOrder added to table Menus");
        }

        // The app itself always assigns a colour on product creation (see ProductsViewModel.BeginAddProduct),
        // so a NULL Color can only come from data imported outside the app (e.g. the MySQL migration tool,
        // which allowed it). Backfill such rows so every view can rely on Products.Color being set.
        int recoloured = connection.Execute("UPDATE Products SET Color = @Default WHERE Color IS NULL;",
            new { Default = DatabaseInitializer.DefaultProductColor }, transaction);
        if (recoloured > 0)
            logger.LogInformation("Migration applied: {ProductCount} product(s) without a colour assigned the default colour", recoloured);
    }

    /// <summary>Step 2: free entries and variable prices.
    /// <list type="bullet">
    /// <item>OrderPositions gets its own Id instead of the (OrderId, ProductId) key, so one product can
    /// appear several times in an order (at different prices). SQLite cannot change a primary key, so the
    /// table is rebuilt. ProductId may now be null (a free entry without product); Kind, ListPriceCents
    /// and Note are new. Existing positions are regular ones, so their list price is what was paid.</item>
    /// <item>Products.VariablePrice: the price is entered at every sale.</item>
    /// <item>SavedAnalyses.IncludeFreeEntries: whether a saved product selection includes the free entries
    /// without product; "all products" always does.</item>
    /// </list></summary>
    private static void ApplyFreeEntriesAndVariablePrices(SqliteConnection connection, SqliteTransaction transaction, ILogger logger)
    {
        connection.Execute("""
            CREATE TABLE OrderPositions_New (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                OrderId INTEGER NOT NULL,
                ProductId INTEGER,
                Kind INTEGER NOT NULL DEFAULT 0,
                Amount INTEGER NOT NULL,
                UnitPriceCents INTEGER NOT NULL,
                ListPriceCents INTEGER,
                Note TEXT,
                FOREIGN KEY (OrderId) REFERENCES Orders(Id),
                FOREIGN KEY (ProductId) REFERENCES Products(Id)
            );

            INSERT INTO OrderPositions_New (OrderId, ProductId, Kind, Amount, UnitPriceCents, ListPriceCents)
            SELECT OrderId, ProductId, 0, Amount, UnitPriceCents, UnitPriceCents
            FROM OrderPositions
            ORDER BY OrderId, ProductId;

            DROP TABLE OrderPositions;
            ALTER TABLE OrderPositions_New RENAME TO OrderPositions;

            -- The old primary key indexed OrderId; positions are still looked up per order.
            CREATE INDEX IX_OrderPositions_OrderId ON OrderPositions (OrderId);

            ALTER TABLE Products ADD COLUMN VariablePrice INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE SavedAnalyses ADD COLUMN IncludeFreeEntries INTEGER NOT NULL DEFAULT 1;
            """, transaction: transaction);
    }
}

/// <summary>One schema step: <see cref="Apply"/> runs inside the transaction that also stores
/// <see cref="Version"/> in the file.</summary>
internal sealed record MigrationStep(int Version, string Description, Action<SqliteConnection, SqliteTransaction, ILogger> Apply);

/// <summary>The database was written by a newer app version: its schema version is higher than any this
/// version knows. It is not touched, since this version would write only part of what the newer schema
/// expects.</summary>
public class DatabaseTooNewException : Exception
{
    public DatabaseTooNewException(int fileVersion, int supportedVersion)
        : base($"The database has schema version {fileVersion}; this app version supports up to {supportedVersion}.")
    {
        FileVersion = fileVersion;
        SupportedVersion = supportedVersion;
    }

    public int FileVersion { get; }
    public int SupportedVersion { get; }
}

/// <summary>A migration step failed. It was rolled back, so the file stays at the version before it.</summary>
public class DatabaseMigrationException : Exception
{
    public DatabaseMigrationException(int failedVersion, string? backupPath, Exception innerException)
        : base($"Migration to schema version {failedVersion} failed: {innerException.Message}", innerException)
    {
        FailedVersion = failedVersion;
        BackupPath = backupPath;
    }

    public int FailedVersion { get; }

    /// <summary>The copy taken before the migration started, if any.</summary>
    public string? BackupPath { get; }
}
