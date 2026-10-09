namespace KerwaKasse.Core.Services;

/// <summary>Backup and whole-database restore. The database is a single SQLite file, so a restore is a
/// file replacement; this service hides the locking, sidecar and schema-migration details from the UI.</summary>
public interface IDatabaseBackupService
{
    /// <summary>Row counts of the live database (for the "what am I overwriting" prompt and the empty check).</summary>
    DatabaseSummary GetSummary();

    /// <summary>Whether <paramref name="filePath"/> is a readable KerwaKasse database (has the expected core
    /// tables) that this app version can restore or import, i.e. not one of a newer version.</summary>
    DatabaseFileCheck CheckDatabaseFile(string filePath);

    /// <summary>Replaces the live database with the file at <paramref name="sourceFilePath"/> and brings
    /// its schema up to date. Irreversible: the previous contents are gone afterwards.</summary>
    void Restore(string sourceFilePath);
}

/// <summary>Result of <see cref="IDatabaseBackupService.CheckDatabaseFile"/>.</summary>
public enum DatabaseFileCheck
{
    Valid,

    /// <summary>Missing, unreadable, or not a KerwaKasse database.</summary>
    Invalid,

    /// <summary>A KerwaKasse database of a newer app version; this one would only write part of it.</summary>
    NewerVersion
}

/// <summary>Row counts of the live database. Used to decide whether the database may be replaced
/// silently (a freshly created, never-used file) and to tell the user what they are about to overwrite.</summary>
public record DatabaseSummary(int Products, int Orders, int Menus, int SavedAnalyses)
{
    /// <summary>True when the database holds no user data at all — the state right after first launch.</summary>
    public bool IsEmpty => Products == 0 && Orders == 0 && Menus == 0 && SavedAnalyses == 0;
}
