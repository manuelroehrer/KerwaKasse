namespace KerwaKasse.Core.Models;

/// <summary>How the price of an order position came about. Stored as a number, so existing values must
/// never change.</summary>
public enum OrderPositionKind
{
    /// <summary>A product at its list price.</summary>
    Regular = 0,

    /// <summary>A product whose price is entered at every sale ("Preis beim Verkauf eingeben").</summary>
    VariablePrice = 1,

    /// <summary>An amount entered at the till ("Freie Eingabe"), either on its own or as a special price
    /// for an existing product.</summary>
    FreeEntry = 2
}
