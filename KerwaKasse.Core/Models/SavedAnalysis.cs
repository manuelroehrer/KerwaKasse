namespace KerwaKasse.Core.Models;

/// <summary>A user-defined, reusable analysis: a product selection plus a period. Generic by design —
/// it stores no domain knowledge, only references to the user's own products. "All products" is kept
/// as the <see cref="AllProducts"/> flag (not a list of every id), so a later-added product is included
/// automatically; only an explicit subset is stored in <see cref="ProductIds"/>.</summary>
public class SavedAnalysis
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>True = analyse all products (the <see cref="ProductIds"/> list is then ignored/empty).</summary>
    public bool AllProducts { get; set; } = true;

    /// <summary>Explicit product selection used when <see cref="AllProducts"/> is false.</summary>
    public List<int> ProductIds { get; set; } = new();

    /// <summary>Whether an explicit selection also covers the free entries without product (they belong to
    /// no product id). Ignored when <see cref="AllProducts"/> is true, which always includes them.</summary>
    public bool IncludeFreeEntries { get; set; } = true;

    public DateTime? From { get; set; }
    public DateTime? To { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Number of selected products. Filled by GetAll for the list display; not a stored column.</summary>
    public int ProductCount { get; set; }
}
