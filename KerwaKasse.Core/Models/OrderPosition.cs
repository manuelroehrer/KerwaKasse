namespace KerwaKasse.Core.Models;

public class OrderPosition
{
    public int Id { get; set; }
    public int OrderId { get; set; }

    /// <summary>The product the position belongs to; null for a free entry without product.</summary>
    public int? ProductId { get; set; }

    public OrderPositionKind Kind { get; set; } = OrderPositionKind.Regular;

    /// <summary>
    /// The product's current name. NOT persisted — it is populated on read via a JOIN on
    /// Product (see SqliteOrderService). Historical orders therefore always reflect the
    /// product's current description; only UnitPrice is snapshotted at order time. Empty for a
    /// free entry without product.
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    public int Amount { get; set; } = 1;

    /// <summary>The price charged per piece, snapshotted at order time.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>The product's normal price at the time of sale: its list price for a regular position,
    /// the suggested price for a variable-price product, the regular price for a special price. Lets the
    /// analysis tell a price change over the years from a price entered at the till. Null without a
    /// product.</summary>
    public decimal? ListPrice { get; set; }

    /// <summary>What a free entry was for; null otherwise, or when it was added without a description.</summary>
    public string? Note { get; set; }

    public decimal Total => Amount * UnitPrice;
}
