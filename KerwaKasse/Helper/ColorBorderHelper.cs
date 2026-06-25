using System.Globalization;
using System.Windows.Media;

namespace KerwaKasse.Helper
{
    /// <summary>Produces the complementary (darkened) border brush for a colour swatch, matching the
    /// look used in the products/history views. The darken factor is hard-coded here (0.7) and the
    /// coloured border is always on, so the analysis view needs no settings service for this.</summary>
    public static class ColorBorderHelper
    {
        public const double DarkenFactor = 0.7;

        private static readonly BrushColoredBorderConverter Converter = new();

        public static Brush Border(Brush fill) =>
            Converter.Convert(new object[] { fill, DarkenFactor, true }, typeof(Brush), null, CultureInfo.InvariantCulture)
                as Brush ?? Brushes.Gray;

        public static Brush Border(string colorString) => Border(ParseBrush(colorString));

        public static Brush ParseBrush(string color)
        {
            if (string.IsNullOrEmpty(color)) return Brushes.LightGray;
            try { return (Brush)new BrushConverter().ConvertFrom(color); }
            catch { return Brushes.LightGray; }
        }
    }
}
