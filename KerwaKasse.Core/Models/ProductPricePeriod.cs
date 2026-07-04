namespace KerwaKasse.Core.Models;

/// <summary>One distinct unit price of a product within an analysis range, together with the first
/// and last time it was sold at that price. Unit prices are snapshots per order position, so a
/// product can carry several of these when its price changed over the years.</summary>
public class ProductPricePeriod
{
    public int ProductId { get; set; }
    public decimal UnitPrice { get; set; }
    public DateTime From { get; set; }
    public DateTime To { get; set; }
}
