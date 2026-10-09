using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;

namespace KerwaKasse.MVVM.Model
{
    /// <summary>One product row in the analysis breakdown table (name, quantity, unit price, revenue,
    /// colour swatch with its complementary border).</summary>
    public class AnalysisSalesRow
    {
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        public string Name { get; init; } = string.Empty;
        public int Amount { get; init; }
        public decimal Revenue { get; init; }

        /// <summary>The unit price when every sale in the range had the same one; null when the price
        /// changed within the range.</summary>
        public decimal? UnitPrice { get; init; }

        /// <summary>The unit price column: the price itself, or "var." instead of a price range so the
        /// column stays narrow in the table and the PDF. The tooltip lists the individual prices.</summary>
        public string UnitPriceText => UnitPrice?.ToString("C", German) ?? "var.";

        /// <summary>True when the price changed within the range; the table then underlines "var." as a
        /// hint that the tooltip holds the individual prices.</summary>
        public bool IsUnitPriceVariable => UnitPrice == null;

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
