namespace KerwaKasse.Core.Models;

public class SalesFigure
{
    public int ProductId { get; set; }
    public string ProductDescription { get; set; } = string.Empty;
    public string? ProductColor { get; set; }
    public int TotalAmount { get; set; }
    public decimal TotalRevenue { get; set; }
}
