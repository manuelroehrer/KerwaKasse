namespace KerwaKasse.Core.Models;

public class Product
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool Available { get; set; } = true;
    public string? Color { get; set; }
    public string? ImagePath { get; set; }
    public int SortOrder { get; set; }
}
