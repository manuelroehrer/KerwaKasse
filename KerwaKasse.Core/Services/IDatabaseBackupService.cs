namespace KerwaKasse.Core.Services;

/// <summary>Backup and whole-database restore. The database is a single SQLite file, so a restore is a
/// file replacement; this service hides the locking, sidecar and schema-migration details from the UI.</summary>
public interface IDatabaseBackupService
{
    /// <summary>Row counts of the live database (for the "what am I overwriting" prompt and the empty check).</summary>
    DatabaseSummary GetSummary();

    /// <summary>True when <paramref name="filePath"/> is a readable SQLite database that looks like a
    /// KerwaKasse database (has the expected core tables).</summary>
    bool IsValidDatabaseFile(string filePath);

    /// <summary>Replaces the live database with the file at <paramref name="sourceFilePath"/> and brings
    /// its schema up to date. Irreversible: the previous contents are gone afterwards.</summary>
    void Restore(string sourceFilePath);
}

/// <summary>Row counts of the live database. Used to decide whether the database may be replaced
/// silently (a freshly created, never-used file) and to tell the user what they are about to overwrite.</summary>
public record DatabaseSummary(int Products, int Orders, int Menus, int SavedAnalyses)
{
    /// <summary>True when the database holds no user data at all — the state right after first launch.</summary>
    public bool IsEmpty => Products == 0 && Orders == 0 && Menus == 0 && SavedAnalyses == 0;
}
