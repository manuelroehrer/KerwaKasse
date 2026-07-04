using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace KerwaKasse.MVVM.Model
{
    /// <summary>One product row in the analysis breakdown table (name, quantity, revenue, colour swatch
    /// with its complementary border).</summary>
    public class AnalysisSalesRow
    {
        public string Name { get; init; } = string.Empty;
        public int Amount { get; init; }
        public decimal Revenue { get; init; }
        public Brush ColorBrush { get; init; } = Brushes.LightGray;
        public Brush ColorBorderBrush { get; init; } = Brushes.Gray;

        /// <summary>Pre-formatted "price + date range" lines for the row tooltip, one per price
        /// segment within the analysed range.</summary>
        public IReadOnlyList<PriceDetailLine> PriceDetails { get; init; } = Array.Empty<PriceDetailLine>();

        public bool HasPriceDetails => PriceDetails.Count > 0;

        public string PriceDetailsHeader => PriceDetails.Count == 1 ? "Einzelpreis" : "Einzelpreise";
    }

    /// <summary>One line of the price-history tooltip: the formatted unit price and its date range,
    /// kept separate so the tooltip can align them as columns.</summary>
    public record PriceDetailLine(string Price, string Range);
}
