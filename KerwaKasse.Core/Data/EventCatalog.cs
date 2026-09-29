using System.Globalization;
using System.Text;
using Dapper;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KerwaKasse.Core.Data;

/// <summary>Keeps one database file per event, all of them in the "database" subfolder of the data folder.
/// The event name is stored inside each file, so it travels with backups and a rename never has to move
/// a file that may be open. Up to version 2.11 the app had a single kerwakasse.db directly in the data
/// folder; it is moved into the database folder on the first start.</summary>
public class EventCatalog : IEventCatalog
{
    /// <summary>Next to "logs", the name makes clear that this folder holds the actual data.</summary>
    public const string DatabaseFolderName = "database";

    /// <summary>The single database of app versions up to 2.11, directly in the data folder.</summary>
    public const string LegacyDatabaseFileName = "kerwakasse.db";

    /// <summary>Name of the first event: the one a fresh installation starts with, and the one the
    /// legacy database becomes (every existing installation used it for the Kerwa).</summary>
    public const string DefaultEventName = "Kerwa";

    private const string ActiveEventSettingKey = "activeEventDatabase";
    private const string EventNameKey = "EventName";

    private readonly string _databaseDir;
    private readonly string _legacyPath;
    private readonly ISettingsService _settings;
    private readonly ILogger<EventCatalog> _logger;
    private readonly Action<string> _deleteFile;

    /// <param name="deleteFile">How a deleted event's file is removed. The app sends it to the recycle
    /// bin as a last safety net; defaults to a plain delete (tests).</param>
    public EventCatalog(string dataDir, ISettingsService settings, ILogger<EventCatalog> logger, Action<string>? deleteFile = null)
    {
        DataFolder = Path.GetFullPath(dataDir);
        _databaseDir = Path.Combine(DataFolder, DatabaseFolderName);
        _legacyPath = Path.Combine(DataFolder, LegacyDatabaseFileName);
        _settings = settings;
        _logger = logger;
        _deleteFile = deleteFile ?? File.Delete;

        Directory.CreateDirectory(_databaseDir);
        MigrateLegacyDatabase();
        ActiveFilePath = ResolveActive();
    }

    public string DataFolder { get; }

    public string ActiveFilePath { get; private set; }

    public ActiveEventFallback? StartupFallback { get; private set; }

    public string? AdoptedLegacyEventName { get; private set; }

    /// <summary>Moves a kerwakasse.db directly in the data folder into the database folder. There is no
    /// version flag: the file's presence alone triggers this, on every start.
    /// <list type="bullet">
    /// <item>Normally it happens once, on the first start after updating from 2.11 or older: the database
    /// folder is still empty, and the file becomes the active "Kerwa" event.</item>
    /// <item>If the database folder already holds events, the file was created again later, e.g. by an
    /// older version started by mistake. Then it must not take over: an empty one (what an older version
    /// creates on its first start) is removed, one with data is added as an additional event and
    /// reported via <see cref="AdoptedLegacyEventName"/>; the active event stays as it is.</item>
    /// </list>
    /// If the file cannot be moved (another program has it open), it stays where it is and is listed
    /// from there for this session; the move is retried on the next start.</summary>
    private void MigrateLegacyDatabase()
    {
        if (!File.Exists(_legacyPath)) return;

        bool firstMigration = !EventFiles().Any();
        string target = NewFilePath(DefaultEventName);
        try
        {
            // Opening the file first lets SQLite roll back a journal left behind by a crash and leaves
            // the database as a single file, so moving (or removing) just that file is safe.
            DatabaseInitializer.Initialize(ConnectionString(_legacyPath), _logger);

            if (!firstMigration && DatabaseBackupService.ReadSummary(ConnectionString(_legacyPath)).IsEmpty)
            {
                File.Delete(_legacyPath);
                _logger.LogInformation("Empty legacy database {LegacyPath} removed; the events already live in {DatabaseDir}", _legacyPath, _databaseDir);
                return;
            }

            File.Move(_legacyPath, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            _logger.LogError(ex, "Legacy database {LegacyPath} could not be moved to {TargetPath}; using it in place for now", _legacyPath, target);
            return;
        }

        // Keep a name the file already carries; otherwise it becomes the "Kerwa" event. A name that is
        // already taken gets a suffix ("Kerwa (2)").
        string name = ReadName(target) ?? DefaultEventName;
        var takenNames = GetAll().Where(e => e.FilePath != target).Select(e => e.Name).ToList();
        string uniqueName = name;
        for (int i = 2; takenNames.Contains(uniqueName, StringComparer.CurrentCultureIgnoreCase); i++)
            uniqueName = $"{name} ({i})";
        WriteName(target, uniqueName);

        if (firstMigration)
        {
            _settings.Set(ActiveEventSettingKey, Path.GetFileName(target));
            _settings.Save();
            _logger.LogInformation("Legacy database moved to {TargetPath} as event \"{EventName}\"", target, uniqueName);
        }
        else
        {
            AdoptedLegacyEventName = uniqueName;
            _logger.LogWarning("Legacy database with data found next to existing events, added as event \"{EventName}\" ({TargetPath}); the active event is unchanged",
                uniqueName, target);
        }
    }

    private string ResolveActive()
    {
        // The setting holds just the file name inside the database folder. GetFileName also accepts a value
        // that still carries a folder, so a hand-edited entry cannot point anywhere else.
        var stored = _settings.Get<string>(ActiveEventSettingKey);
        string? missingFile = null;
        if (!string.IsNullOrEmpty(stored))
        {
            var path = Path.Combine(_databaseDir, Path.GetFileName(stored));
            if (IsCatalogPath(path) && File.Exists(path))
                return path;

            missingFile = Path.GetFileName(stored);
        }

        // The legacy file is only still there if moving it failed just now.
        if (File.Exists(_legacyPath))
            return _legacyPath;

        // The last active event is gone (e.g. its file was deleted by hand): continue with the event that
        // was worked on most recently. Silently creating an empty event while others exist would look like
        // data loss.
        var latest = EventFiles().OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (latest != null)
        {
            SetActive(latest);
            if (missingFile != null)
            {
                _logger.LogWarning("Active event database {MissingFile} not found, opened {FilePath} instead", missingFile, latest);
                StartupFallback = new ActiveEventFallback(missingFile, NewEventCreated: false);
            }
            return latest;
        }

        // No event at all (fresh installation, or every file was removed): start with one empty event.
        var created = Create(DefaultEventName);
        SetActive(created.FilePath);
        if (missingFile != null)
        {
            _logger.LogWarning("Active event database {MissingFile} not found and no other event left, created {FilePath}", missingFile, created.FilePath);
            StartupFallback = new ActiveEventFallback(missingFile, NewEventCreated: true);
        }
        return created.FilePath;
    }

    public IReadOnlyList<EventDatabase> GetAll()
    {
        var files = EventFiles().ToList();
        if (File.Exists(_legacyPath))
            files.Add(_legacyPath);

        return files.Select(Describe)
                    .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
    }

    private IEnumerable<string> EventFiles() =>
        Directory.Exists(_databaseDir) ? Directory.GetFiles(_databaseDir, "*.db").Where(IsCatalogPath) : [];

    public void SetActive(string filePath)
    {
        var path = Path.GetFullPath(filePath);
        if (!IsCatalogPath(path) || !File.Exists(path))
            throw new ArgumentException($"'{filePath}' is not an event database of this catalog.", nameof(filePath));

        // The file may have been created by an older app version: bring it up to date before any
        // service touches it.
        DatabaseInitializer.Initialize(ConnectionString(path), _logger);

        ActiveFilePath = path;
        _settings.Set(ActiveEventSettingKey, Path.GetFileName(path));
        _settings.Save();

        _logger.LogInformation("Active event set to {FilePath}", path);
    }

    public EventDatabase Create(string name)
    {
        name = ValidateName(name);
        var path = NewFilePath(name);

        try
        {
            DatabaseInitializer.Initialize(ConnectionString(path), _logger);
            WriteName(path, name);
        }
        catch
        {
            // Don't leave a half-built event behind that would then show up in the list.
            SqliteConnection.ClearAllPools();
            TryDelete(path);
            throw;
        }

        _logger.LogInformation("Event \"{EventName}\" created in {FilePath}", name, path);
        return Describe(path);
    }

    public EventDatabase Import(string sourceFilePath, string name)
    {
        name = ValidateName(name);
        var path = NewFilePath(name);

        File.Copy(sourceFilePath, path);
        try
        {
            // An imported backup may come from an older app version.
            DatabaseInitializer.Initialize(ConnectionString(path), _logger);
            WriteName(path, name);
        }
        catch
        {
            SqliteConnection.ClearAllPools();
            TryDelete(path);
            throw;
        }

        _logger.LogInformation("Event \"{EventName}\" imported from {SourceFilePath} into {FilePath}", name, sourceFilePath, path);
        return Describe(path);
    }

    public void Rename(string filePath, string newName)
    {
        newName = ValidateName(newName);
        var path = Path.GetFullPath(filePath);
        var oldName = Describe(path).Name;

        // Make sure the Metadata table exists (older, not yet opened files).
        DatabaseInitializer.Initialize(ConnectionString(path), _logger);
        WriteName(path, newName);

        _logger.LogInformation("Event \"{OldName}\" renamed to \"{NewName}\" ({FilePath})", oldName, newName, path);
    }

    public void Delete(string filePath)
    {
        var path = Path.GetFullPath(filePath);
        if (string.Equals(path, ActiveFilePath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The active event cannot be deleted.");
        if (!IsCatalogPath(path))
            throw new ArgumentException($"'{filePath}' is not an event database of this catalog.", nameof(filePath));

        var name = Describe(path).Name;

        // The event may have been active earlier in this session; its pooled connections would still
        // hold the file open.
        SqliteConnection.ClearAllPools();
        _deleteFile(path);
        foreach (var sidecar in new[] { "-wal", "-shm", "-journal" })
            TryDelete(path + sidecar);

        _logger.LogInformation("Event \"{EventName}\" deleted ({FilePath})", name, path);
    }

    private EventDatabase Describe(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        int products = 0, orders = 0;
        DateTime? firstOrder = null, lastOrder = null;

        try
        {
            using var connection = OpenReadOnly(path);
            name = ReadName(connection) ?? name;
            products = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM Products");
            var stats = connection.QuerySingle<OrderStatsRow>(
                "SELECT COUNT(*) AS OrderCount, MIN(OrderTime) AS FirstOrder, MAX(OrderTime) AS LastOrder FROM Orders");
            orders = (int)stats.OrderCount;
            firstOrder = ParseOrderTime(stats.FirstOrder);
            lastOrder = ParseOrderTime(stats.LastOrder);
        }
        catch (Exception ex)
        {
            // A damaged or foreign file still shows up (under its file name), so it can be deleted.
            _logger.LogWarning(ex, "Event database {FilePath} could not be read", path);
        }

        return new EventDatabase(path, name, products, orders, firstOrder, lastOrder);
    }

    private string? ReadName(string path)
    {
        try
        {
            using var connection = OpenReadOnly(path);
            return ReadName(connection);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Event name of {FilePath} could not be read", path);
            return null;
        }
    }

    private static string? ReadName(SqliteConnection connection)
    {
        bool hasTable = connection.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Metadata'") > 0;
        if (!hasTable) return null;

        var name = connection.ExecuteScalar<string?>(
            "SELECT Value FROM Metadata WHERE Key = @Key", new { Key = EventNameKey });
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static void WriteName(string path, string name)
    {
        using var connection = new SqliteConnection(ConnectionString(path));
        connection.Execute(
            "INSERT INTO Metadata (Key, Value) VALUES (@Key, @Value) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value",
            new { Key = EventNameKey, Value = name });
    }

    // Only *.db files directly in the database folder belong to the catalog (plus the legacy file while
    // it could not be moved yet); this keeps a manipulated settings entry from pointing the app at an
    // arbitrary file.
    private bool IsCatalogPath(string path)
    {
        path = Path.GetFullPath(path);
        if (string.Equals(path, _legacyPath, StringComparison.OrdinalIgnoreCase))
            return File.Exists(_legacyPath);

        return string.Equals(Path.GetDirectoryName(path), _databaseDir, StringComparison.OrdinalIgnoreCase)
               && string.Equals(Path.GetExtension(path), ".db", StringComparison.OrdinalIgnoreCase);
    }

    private string NewFilePath(string name)
    {
        string slug = Slugify(name);
        string path = Path.Combine(_databaseDir, slug + ".db");
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(_databaseDir, $"{slug}-{i}.db");
        return path;
    }

    /// <summary>Readable, file-system-safe file name derived from the event name ("Herbstkerwa 2026"
    /// becomes "herbstkerwa-2026"). Only used once when the file is created; renaming the event later
    /// keeps the file name.</summary>
    public static string Slugify(string name)
    {
        var text = name.Trim().ToLowerInvariant()
            .Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue").Replace("ß", "ss");

        // Strip remaining accents (é → e) before dropping everything that is not a plain letter/digit.
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(c is >= 'a' and <= 'z' or >= '0' and <= '9' ? c : '-');
        }

        var slug = string.Join('-', builder.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        return slug.Length == 0 ? "veranstaltung" : slug;
    }

    private static string ValidateName(string name)
    {
        name = name?.Trim() ?? string.Empty;
        if (name.Length == 0)
            throw new ArgumentException("The event name must not be empty.", nameof(name));
        return name;
    }

    private static DateTime? ParseOrderTime(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

    // Pooling off: these are short, one-off accesses to files that may be moved or deleted right
    // afterwards, so no handle may linger in the pool.
    private static string ConnectionString(string path) =>
        new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();

    private static SqliteConnection OpenReadOnly(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString());
        connection.Open();
        return connection;
    }

    private void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not remove {FilePath}", path);
        }
    }

    private sealed class OrderStatsRow
    {
        public long OrderCount { get; set; }
        public string? FirstOrder { get; set; }
        public string? LastOrder { get; set; }
    }
}
