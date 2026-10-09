namespace KerwaKasse.Core.Models;

/// <summary>What a row of the analysis stands for.</summary>
public enum SalesFigureKind
{
    /// <summary>A product at its price: the regular sales and, for a product with a variable price,
    /// the sales at an entered price.</summary>
    Product = 0,

    /// <summary>The free entries for a product, i.e. its sales at a special price.</summary>
    SpecialPrice = 1,

    /// <summary>All free entries without a product.</summary>
    FreeEntries = 2
}

public class SalesFigure
{
    public SalesFigureKind Kind { get; set; }

    /// <summary>The product; null for <see cref="SalesFigureKind.FreeEntries"/>.</summary>
    public int? ProductId { get; set; }

    /// <summary>The product's name; empty for <see cref="SalesFigureKind.FreeEntries"/>.</summary>
    public string ProductName { get; set; } = string.Empty;
    public string? ProductColor { get; set; }
    public int TotalAmount { get; set; }
    public decimal TotalRevenue { get; set; }

    /// <summary>Lowest and highest unit price the product was sold at within the range. Equal when
    /// all its sales share one price, which the analysis then shows as the product's unit price.</summary>
    public decimal MinUnitPrice { get; set; }
    public decimal MaxUnitPrice { get; set; }

    /// <summary>The pieces sold at an entered price (a product with a variable price), with their
    /// revenue and lowest/highest price; all 0 when there were none.</summary>
    public int VariablePriceAmount { get; set; }
    public decimal VariablePriceRevenue { get; set; }
    public decimal VariablePriceMin { get; set; }
    public decimal VariablePriceMax { get; set; }
}
