namespace KerwaKasse.Core.Models;

public class SalesFigure
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public string? ProductColor { get; set; }
    public int TotalAmount { get; set; }
    public decimal TotalRevenue { get; set; }

    /// <summary>Lowest and highest unit price the product was sold at within the range. Equal when
    /// all its sales share one price, which the analysis then shows as the product's unit price.</summary>
    public decimal MinUnitPrice { get; set; }
    public decimal MaxUnitPrice { get; set; }
}
