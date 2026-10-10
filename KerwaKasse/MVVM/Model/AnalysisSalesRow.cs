using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Media;
using KerwaKasse.Core.Models;

namespace KerwaKasse.MVVM.Model
{
    /// <summary>One row in the analysis breakdown table (name, quantity, unit price, revenue, colour
    /// swatch with its complementary border): a product, a product's special prices or the free entries.
    /// The latter two can be expanded into their <see cref="Details"/>.</summary>
    public class AnalysisSalesRow : PropertyChangedBase
    {
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        public SalesFigureKind Kind { get; init; }

        /// <summary>The product; null for the free entries.</summary>
        public int? ProductId { get; init; }

        public string Name { get; init; } = string.Empty;
        public int Amount { get; init; }
        public decimal Revenue { get; init; }

        /// <summary>The unit price when every sale in the range had the same one; null when the price
        /// changed within the range.</summary>
        public decimal? UnitPrice { get; init; }

        /// <summary>The unit price column: the price itself, or "var." instead of a price range so the
        /// column stays narrow in the table and the PDF. The tooltip or the details list the prices.</summary>
        public string UnitPriceText => UnitPrice?.ToString("C", German) ?? "var.";

        /// <summary>True when the table underlines "var." as a hint that the tooltip holds the individual
        /// prices (rows with details show them when expanded instead).</summary>
        public bool IsUnitPriceVariable => UnitPrice == null && HasPriceDetails;

        public Brush ColorBrush { get; init; } = Brushes.LightGray;
        public Brush ColorBorderBrush { get; init; } = Brushes.Gray;

        /// <summary>Pre-formatted "price + date range" lines for the row tooltip, one per price
        /// segment within the analysed range ("ca." for a suggested price).</summary>
        public IReadOnlyList<PriceDetailLine> PriceDetails { get; init; } = Array.Empty<PriceDetailLine>();

        /// <summary>For a product with a variable price: the range and average of the entered prices
        /// (e.g. "6,00 – 8,50 € · Ø 7,67 €"); null otherwise.</summary>
        public string EnteredPrices { get; init; }

        public bool HasEnteredPrices => EnteredPrices != null;

        public bool HasPriceDetails => PriceDetails.Count > 0 || HasEnteredPrices;

        public string PriceDetailsHeader => PriceDetails.Count == 1 ? "Einzelpreis" : "Einzelpreise";

        /// <summary>What a special price row or the free entries row consists of, per description and price.</summary>
        public IReadOnlyList<AnalysisDetailRow> Details { get; init; } = Array.Empty<AnalysisDetailRow>();

        public bool HasDetails => Details.Count > 0;

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
        }

        /// <summary>Identifies the row across reloads and re-sorts, so an expanded row stays expanded.</summary>
        public string Key => $"{Kind}:{ProductId}";
    }

    /// <summary>One line of the price-history tooltip: the formatted unit price and its date range,
    /// kept separate so the tooltip can align them as columns.</summary>
    public record PriceDetailLine(string Price, string Range);

    /// <summary>One line below an expanded special price or free entries row: entries with the same
    /// description and price.</summary>
    public class AnalysisDetailRow
    {
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

        /// <summary>The description, or "Ohne Angabe".</summary>
        public string Name { get; init; } = string.Empty;

        /// <summary>Entered without description; highlighted like at the till.</summary>
        public bool IsWithoutDescription { get; init; }

        public int Amount { get; init; }
        public decimal UnitPrice { get; init; }
        public decimal Revenue { get; init; }

        public string UnitPriceText => UnitPrice.ToString("C", German);
    }
}
