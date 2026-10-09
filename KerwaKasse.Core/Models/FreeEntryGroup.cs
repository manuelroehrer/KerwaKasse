namespace KerwaKasse.Core.Models;

/// <summary>Free entries within an analysis range that share product, description and price: the
/// lines a special price row or the "Freie Eingabe" row of the analysis consists of.</summary>
public class FreeEntryGroup
{
    /// <summary>The product the entries are a special price for; null for free entries without one.</summary>
    public int? ProductId { get; set; }

    /// <summary>The description; null for entries without one.</summary>
    public string? Note { get; set; }

    public decimal UnitPrice { get; set; }
    public int TotalAmount { get; set; }
    public decimal TotalRevenue { get; set; }
}
