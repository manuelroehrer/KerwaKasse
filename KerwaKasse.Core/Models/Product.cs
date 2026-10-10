namespace KerwaKasse.Core.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Price in euros. Stored in the DB as integer cents (PriceCents).</summary>
    public decimal Price { get; set; }

    public bool Available { get; set; } = true;

    /// <summary>The price is entered at every sale ("Preis beim Verkauf eingeben"); <see cref="Price"/>
    /// is then only the suggestion the entry starts from.</summary>
    public bool VariablePrice { get; set; }
    public string? Color { get; set; }
    public int SortOrder { get; set; }
}
