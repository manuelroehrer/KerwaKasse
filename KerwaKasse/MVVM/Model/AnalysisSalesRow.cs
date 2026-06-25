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
    }
}
