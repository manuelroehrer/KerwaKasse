namespace KerwaKasse.Core.Models;

public class Order
{
    public int Id { get; set; }
    public DateTime OrderTime { get; set; }
    public List<OrderPosition> Positions { get; set; } = [];

    public decimal Total => Positions.Sum(p => p.Amount * p.UnitPrice);

    public string ShortDescription =>
        string.Join(", ", Positions.Select(p => p.ProductName));
}
