using KerwaKasse.Core.Models;

namespace KerwaKasse.Core.Services;

/// <summary>The last active event's file (<paramref name="MissingFileName"/>) was not found at startup.
/// Another event was opened instead, or a new empty one created when none was left.</summary>
public record ActiveEventFallback(string MissingFileName, bool NewEventCreated);

/// <summary>The events (Veranstaltungen) the app knows about. Each event is a self-contained database
/// file with its own products, menus, orders and saved analyses; exactly one of them is active and
/// receives everything booked at the till.</summary>
public interface IEventCatalog
{
    /// <summary>The app's data folder (settings, logs and the "database" folder with the event databases).</summary>
    string DataFolder { get; }

    /// <summary>Full path of the database the app currently works on.</summary>
    string ActiveFilePath { get; }

    /// <summary>Set when the event that was active last time could not be found at startup and another
    /// one was opened instead, so the app can tell the user; null otherwise.</summary>
    ActiveEventFallback? StartupFallback { get; }

    /// <summary>Set when a kerwakasse.db of an older app version turned up again next to existing events
    /// (e.g. an older version was started by mistake) and, since it held data, was added as an
    /// additional event under this name; null otherwise. The active event is not changed by that.</summary>
    string? AdoptedLegacyEventName { get; }

    /// <summary>All events, sorted by name.</summary>
    IReadOnlyList<EventDatabase> GetAll();

    /// <summary>Makes <paramref name="filePath"/> the active event and remembers it for the next start.
    /// The caller is responsible for reconnecting the services to the new file.</summary>
    void SetActive(string filePath);

    /// <summary>Creates a new, empty event.</summary>
    EventDatabase Create(string name);

    /// <summary>Adds a copy of an existing KerwaKasse database file (e.g. a backup) as a new event.
    /// The file must have been validated before (see <see cref="IDatabaseBackupService.IsValidDatabaseFile"/>).</summary>
    EventDatabase Import(string sourceFilePath, string name);

    void Rename(string filePath, string newName);

    /// <summary>Removes an event and its database file. The active event cannot be deleted.</summary>
    void Delete(string filePath);
}
