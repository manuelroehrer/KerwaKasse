using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

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
}
