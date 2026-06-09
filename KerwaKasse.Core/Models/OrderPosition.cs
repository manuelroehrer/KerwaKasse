namespace KerwaKasse.Core.Models;

public class OrderPosition
{
    public int OrderId { get; set; }
    public int ProductId { get; set; }

    /// <summary>
    /// The product's current name. NOT persisted — it is populated on read via a JOIN on
    /// Product (see SqliteOrderService). Historical orders therefore always reflect the
    /// product's current description; only UnitPrice is snapshotted at order time.
    /// </summary>
    public string ProductName { get; set; } = string.Empty;

    public int Amount { get; set; } = 1;
    public decimal UnitPrice { get; set; }

    public decimal Total => Amount * UnitPrice;
}
