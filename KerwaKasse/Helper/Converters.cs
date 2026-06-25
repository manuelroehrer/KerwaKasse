using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace KerwaKasse.Helper
{
    public class DoubleToGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
                return new GridLength(d);
            return new GridLength(1, GridUnitType.Star);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is GridLength gl)
                return gl.Value;
            return 0.0;
        }
    }

    public class DoubleToUniformThicknessConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double d)
                return new Thickness(d);
            return new Thickness(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Thickness t)
                return t.Left;
            return 0.0;
        }
    }

    /// <summary>
    /// Returns a darkened, saturated version of the card color for use as a text outline or border.
    /// Without ConverterParameter: light backgrounds get a visible stroke; dark backgrounds get Transparent.
    /// With ConverterParameter="always": always returns a darkened color (for card borders).
    /// </summary>
    public class BrushToStrokeBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is SolidColorBrush brush)
            {
                var c = brush.Color;
                double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

                bool alwaysVisible = parameter is string s && s.Equals("always", StringComparison.OrdinalIgnoreCase);

                if (!alwaysVisible && luminance <= 0.40)
                    return Brushes.Transparent;

                // Convert RGB → HSL
                double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
                double max = Math.Max(r, Math.Max(g, b));
                double min = Math.Min(r, Math.Min(g, b));
                double h = 0, s2 = 0, l = (max + min) / 2.0;

                if (max != min)
                {
                    double d = max - min;
                    s2 = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
                    if (max == r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6.0;
                    else if (max == g) h = ((b - r) / d + 2) / 6.0;
                    else h = ((r - g) / d + 4) / 6.0;
                }

                // Darken: force lightness low, boost saturation.
                // For achromatic colors (grey/white, s≈0), preserve s=0 so we output a grey
                // rather than drifting towards red (h=0 + boosted saturation = red tinge).
                double newL = alwaysVisible
                    ? Math.Clamp(luminance * 0.55, 0.06, 0.28)
                    : Math.Clamp(0.22 + (1.0 - luminance) * 0.08, 0.18, 0.30);
                double newS = s2 < 0.05 ? 0.0 : Math.Clamp(s2 + 0.30, 0.50, 1.0);

                var (nr, ng, nb) = HslToRgb(h, newS, newL);
                return new SolidColorBrush(Color.FromRgb(
                    (byte)(nr * 255), (byte)(ng * 255), (byte)(nb * 255)));
            }
            return Brushes.Transparent;
        }

        private static (double r, double g, double b) HslToRgb(double h, double s, double l)
        {
            if (s == 0) return (l, l, l);
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            return (HueToRgb(p, q, h + 1.0 / 3), HueToRgb(p, q, h), HueToRgb(p, q, h - 1.0 / 3));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// IMultiValueConverter for colored card borders.
    /// Values[0] = SolidColorBrush (accent color)
    /// Values[1] = double darkenFactor (0.0 = original color, 1.0 = maximum darkening)
    /// Values[2] = bool useColoredBorder (optional, default true. When false, returns #D8D8D8)
    /// </summary>
    public class BrushColoredBorderConverter : System.Windows.Data.IMultiValueConverter
    {
        private static readonly SolidColorBrush GreyBrush = new SolidColorBrush(Color.FromRgb(0xD8, 0xD8, 0xD8));

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(values[0] is SolidColorBrush brush)) return Brushes.Transparent;

            // Check optional UseColoredBorder flag
            if (values.Length > 2 && values[2] is bool useColored && !useColored)
                return GreyBrush;

            double factor = values.Length > 1 && values[1] is double d ? Math.Clamp(d, 0.0, 1.0) : 1.0;

            var c = brush.Color;
            double lum = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double h = 0, s = 0, l = (max + min) / 2.0;

            if (max != min)
            {
                double delta = max - min;
                s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);
                if (max == r) h = ((g - b) / delta + (g < b ? 6 : 0)) / 6.0;
                else if (max == g) h = ((b - r) / delta + 2) / 6.0;
                else h = ((r - g) / delta + 4) / 6.0;
            }

            // Target darkened values
            double targetL = Math.Clamp(lum * 0.55, 0.06, 0.28);
            double targetS = s < 0.05 ? 0.0 : Math.Clamp(s + 0.30, 0.50, 1.0);

            // Interpolate between original and darkened by factor
            double newL = l + (targetL - l) * factor;
            double newS = s + (targetS - s) * factor;

            var (nr, ng, nb) = HslToRgb(h, newS, newL);
            return new SolidColorBrush(Color.FromRgb(
                (byte)Math.Clamp(nr * 255, 0, 255),
                (byte)Math.Clamp(ng * 255, 0, 255),
                (byte)Math.Clamp(nb * 255, 0, 255)));
        }

        private static (double r, double g, double b) HslToRgb(double h, double s, double l)
        {
            if (s == 0) return (l, l, l);
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            return (HueToRgb(p, q, h + 1.0 / 3), HueToRgb(p, q, h), HueToRgb(p, q, h - 1.0 / 3));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Returns Collapsed when the value is true, Visible when false.
    /// Inverse of the built-in BooleanToVisibilityConverter.
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? Visibility.Collapsed : Visibility.Visible;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Corner radius for one segment of a <see cref="Controls.SegmentedControl"/> so that only the
    /// outer corners are rounded: the first segment rounds its left side, the last its right side,
    /// inner segments stay square (a single item rounds all four).
    /// values[0] = the item, values[1] = the owning ItemsControl; ConverterParameter = radius (default 4).
    /// </summary>
    public class SegmentCornerRadiusConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            double r = 4;
            if (parameter is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var p))
                r = p;

            if (values.Length < 2 || values[1] is not ItemsControl owner) return new CornerRadius(0);
            int index = owner.Items.IndexOf(values[0]);
            if (index < 0) return new CornerRadius(0);

            bool first = index == 0;
            bool last = index == owner.Items.Count - 1;
            if (first && last) return new CornerRadius(r);
            if (first) return new CornerRadius(r, 0, 0, r);
            if (last) return new CornerRadius(0, r, r, 0);
            return new CornerRadius(0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Visible for every segment of a <see cref="Controls.SegmentedControl"/> except the last one —
    /// used to draw the vertical divider between segments without a trailing line on the right edge.
    /// values[0] = the item, values[1] = the owning ItemsControl.
    /// </summary>
    public class SegmentDividerVisibilityConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[1] is not ItemsControl owner) return Visibility.Collapsed;
            int index = owner.Items.IndexOf(values[0]);
            return index >= 0 && index < owner.Items.Count - 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
