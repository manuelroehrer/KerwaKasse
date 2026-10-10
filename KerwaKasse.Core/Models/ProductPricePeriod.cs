namespace KerwaKasse.Core.Models;

/// <summary>One distinct price of a product within an analysis range, together with the first and
/// last time it was sold at that price. Prices are snapshots per order position, so a product can
/// carry several of these when its price changed over the years. For a product with a variable price
/// it is the suggested price (<see cref="IsVariablePrice"/>), not the entered one.</summary>
public class ProductPricePeriod
{
    public int ProductId { get; set; }
    public decimal UnitPrice { get; set; }

    /// <summary>True when the price was only the suggestion and the actual price was entered at the sale.</summary>
    public bool IsVariablePrice { get; set; }

    public DateTime From { get; set; }
    public DateTime To { get; set; }
}
