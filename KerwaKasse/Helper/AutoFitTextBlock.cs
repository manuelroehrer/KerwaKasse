using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace KerwaKasse.Helper
{
    public static class AutoFitTextBlock
    {
        public static readonly DependencyProperty EnabledProperty =
            DependencyProperty.RegisterAttached(
                "Enabled",
                typeof(bool),
                typeof(AutoFitTextBlock),
                new PropertyMetadata(false, OnEnabledChanged));

        public static readonly DependencyProperty MinFontSizeProperty =
            DependencyProperty.RegisterAttached(
                "MinFontSize",
                typeof(double),
                typeof(AutoFitTextBlock),
                new PropertyMetadata(11.0, OnFitSettingChanged));

        public static readonly DependencyProperty MaxFontSizeProperty =
            DependencyProperty.RegisterAttached(
                "MaxFontSize",
                typeof(double),
                typeof(AutoFitTextBlock),
                new PropertyMetadata(double.NaN, OnFitSettingChanged));

        private static readonly DependencyProperty OriginalFontSizeProperty =
            DependencyProperty.RegisterAttached(
                "OriginalFontSize",
                typeof(double),
                typeof(AutoFitTextBlock),
                new PropertyMetadata(double.NaN));

        private static readonly DependencyProperty IsFittingProperty =
            DependencyProperty.RegisterAttached(
                "IsFitting",
                typeof(bool),
                typeof(AutoFitTextBlock),
                new PropertyMetadata(false));

        public static bool GetEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(EnabledProperty);
        }

        public static void SetEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(EnabledProperty, value);
        }

        public static double GetMinFontSize(DependencyObject obj)
        {
            return (double)obj.GetValue(MinFontSizeProperty);
        }

        public static void SetMinFontSize(DependencyObject obj, double value)
        {
            obj.SetValue(MinFontSizeProperty, value);
        }

        public static double GetMaxFontSize(DependencyObject obj)
        {
            return (double)obj.GetValue(MaxFontSizeProperty);
        }

        public static void SetMaxFontSize(DependencyObject obj, double value)
        {
            obj.SetValue(MaxFontSizeProperty, value);
        }

        private static double GetOriginalFontSize(DependencyObject obj)
        {
            return (double)obj.GetValue(OriginalFontSizeProperty);
        }

        private static void SetOriginalFontSize(DependencyObject obj, double value)
        {
            obj.SetValue(OriginalFontSizeProperty, value);
        }

        private static bool GetIsFitting(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsFittingProperty);
        }

        private static void SetIsFitting(DependencyObject obj, bool value)
        {
            obj.SetValue(IsFittingProperty, value);
        }

        private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock textBlock)
                return;

            if ((bool)e.NewValue)
            {
                SetOriginalFontSize(textBlock, textBlock.FontSize);
                textBlock.Loaded += OnTextBlockLayoutChanged;
                textBlock.SizeChanged += OnTextBlockLayoutChanged;

                DependencyPropertyDescriptor
                    .FromProperty(TextBlock.TextProperty, typeof(TextBlock))
                    .AddValueChanged(textBlock, OnTextChanged);

                Fit(textBlock);
            }
            else
            {
                textBlock.Loaded -= OnTextBlockLayoutChanged;
                textBlock.SizeChanged -= OnTextBlockLayoutChanged;

                DependencyPropertyDescriptor
                    .FromProperty(TextBlock.TextProperty, typeof(TextBlock))
                    .RemoveValueChanged(textBlock, OnTextChanged);
            }
        }

        private static void OnFitSettingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBlock textBlock && GetEnabled(textBlock))
                Fit(textBlock);
        }

        private static void OnTextChanged(object sender, EventArgs e)
        {
            if (sender is TextBlock textBlock && GetEnabled(textBlock))
                Fit(textBlock);
        }

        private static void OnTextBlockLayoutChanged(object sender, RoutedEventArgs e)
        {
            if (sender is TextBlock textBlock && GetEnabled(textBlock))
                Fit(textBlock);
        }

        private static void Fit(TextBlock textBlock)
        {
            if (GetIsFitting(textBlock) || !textBlock.IsLoaded)
                return;

            // Floor the width: with UseLayoutRounding in the ancestor chain the actually
            // rendered width can be slightly narrower than ActualWidth, which would make
            // a size that fits the probe still wrap one line further in the live control.
            double availableWidth = Math.Floor(textBlock.ActualWidth);
            double availableHeight = textBlock.MaxHeight;

            if (availableWidth <= 0 || double.IsInfinity(availableWidth) ||
                availableHeight <= 0 || double.IsInfinity(availableHeight))
                return;

            double maxFontSize = GetMaxFontSize(textBlock);
            if (double.IsNaN(maxFontSize) || maxFontSize <= 0)
                maxFontSize = GetOriginalFontSize(textBlock);

            double minFontSize = Math.Max(1.0, GetMinFontSize(textBlock));
            if (minFontSize > maxFontSize)
                minFontSize = maxFontSize;

            SetIsFitting(textBlock, true);
            try
            {
                if (Fits(textBlock, maxFontSize, availableWidth, availableHeight))
                {
                    textBlock.FontSize = maxFontSize;
                    return;
                }

                double low = minFontSize;
                double high = maxFontSize;

                for (int i = 0; i < 10; i++)
                {
                    double mid = (low + high) / 2;
                    if (Fits(textBlock, mid, availableWidth, availableHeight))
                        low = mid;
                    else
                        high = mid;
                }

                // Round DOWN: the binary search converges right at the size where the text
                // barely fits, so rounding up by even 0.05 can push a word onto an extra
                // line that gets clipped by MaxHeight.
                textBlock.FontSize = Math.Max(minFontSize, Math.Floor(low * 10) / 10);
            }
            finally
            {
                SetIsFitting(textBlock, false);
            }
        }

        private static bool Fits(TextBlock source, double fontSize, double availableWidth, double availableHeight)
        {
            Size size = Measure(source, fontSize, availableWidth);
            if (size.Width > availableWidth + 0.5 || size.Height > availableHeight + 0.5)
                return false;

            // Reject sizes at which a single word only fits by breaking mid-word:
            // WPF's emergency line break keeps such text inside the bounds, so the
            // wrapped measurement above cannot detect it.
            foreach (string word in source.Text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (Measure(source, fontSize, double.PositiveInfinity, word).Width > availableWidth + 0.5)
                    return false;
            }

            return true;
        }

        private static Size Measure(TextBlock source, double fontSize, double availableWidth, string text = null)
        {
            var probe = new TextBlock
            {
                Text = text ?? source.Text,
                FontFamily = source.FontFamily,
                FontSize = fontSize,
                FontStretch = source.FontStretch,
                FontStyle = source.FontStyle,
                FontWeight = source.FontWeight,
                LineHeight = source.LineHeight,
                TextAlignment = source.TextAlignment,
                TextTrimming = source.TextTrimming,
                TextWrapping = source.TextWrapping
            };

            probe.Measure(new Size(availableWidth, double.PositiveInfinity));
            return probe.DesiredSize;
        }
    }
}
