namespace KerwaKasse.Core.Models;

public class OrderPosition
{
    public int OrderId { get; set; }
    public int ProductId { get; set; }
    public string ProductDescription { get; set; } = string.Empty;
    public int Amount { get; set; } = 1;
    public decimal UnitPrice { get; set; }

    public decimal Total => Amount * UnitPrice;
}
