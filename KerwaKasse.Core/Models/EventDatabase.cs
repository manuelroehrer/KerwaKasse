namespace KerwaKasse.Core.Models;

/// <summary>One event (Veranstaltung) and the database file that holds all of its data, together with
/// a few key figures for the event list. <paramref name="RequiresNewerVersion"/> marks a file written by a
/// newer app version, which this one cannot open.</summary>
public record EventDatabase(
    string FilePath,
    string Name,
    int Products,
    int Orders,
    DateTime? FirstOrder,
    DateTime? LastOrder,
    bool RequiresNewerVersion = false);
