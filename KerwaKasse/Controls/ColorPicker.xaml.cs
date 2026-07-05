using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KerwaKasse.Controls
{
    /// <summary>
    /// Own colour picker (replaces the Xceed one): a preset palette on top, a saturation/brightness
    /// square with a hue bar below it, and an editable hex field. Mouse- and touch-draggable. The
    /// colour is exposed as <see cref="SelectedHex"/> ("#AARRGGBB", alpha always FF) so it binds
    /// straight to the product's colour string. The palette tile matching the current colour is
    /// marked with an accent ring.
    /// </summary>
    public partial class ColorPicker : UserControl
    {
        private const double SpectrumWidth = 336;
        private const double SpectrumHeight = 190;

        public static readonly DependencyProperty SelectedHexProperty = DependencyProperty.Register(
            nameof(SelectedHex), typeof(string), typeof(ColorPicker),
            new FrameworkPropertyMetadata("#FFCCCCCC", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                (d, e) => ((ColorPicker)d).OnSelectedHexChanged(e.NewValue as string)));

        public string SelectedHex
        {
            get => (string)GetValue(SelectedHexProperty);
            set => SetValue(SelectedHexProperty, value);
        }

        // The authoritative current colour. HSV below is derived state for the thumbs/spectrum;
        // keeping the exact RGB avoids HSV→RGB round-trip drift (±1 per channel), which would
        // corrupt values from outside and break the palette's selection match.
        private Color _color = Color.FromRgb(0xCC, 0xCC, 0xCC);
        private double _hue;          // 0..360
        private double _saturation;   // 0..1
        private double _value = 1.0;  // 0..1

        private readonly List<PaletteItem> _palette;
        private bool _spectrumDragging;
        private bool _hueDragging;
        private bool _suppressHexPushback; // guards SelectedHex → visuals → SelectedHex loops
        private bool _updatingHexBox;

        public ColorPicker()
        {
            InitializeComponent();
            _palette = BuildPalette();
            PaletteItems.ItemsSource = _palette;
            Loaded += (_, _) => { ApplyHex(SelectedHex); };
        }

        // ── DP plumbing ──
        private void OnSelectedHexChanged(string newValue)
        {
            if (_suppressHexPushback) return;
            ApplyHex(newValue);
        }

        /// <summary>Parses any of #RGB/#RRGGBB/#AARRGGBB into the colour state and repaints.</summary>
        private void ApplyHex(string hex)
        {
            if (TryParse(hex, out var color))
            {
                _color = color;
                (_hue, _saturation, _value) = ColorToHsv(color);
                UpdateVisuals();
            }
        }

        /// <summary>Publishes the current colour to <see cref="SelectedHex"/> and repaints.
        /// Alpha stays FF — product colours are always opaque.</summary>
        private void PushColor()
        {
            _suppressHexPushback = true;
            SetCurrentValue(SelectedHexProperty, $"#FF{_color.R:X2}{_color.G:X2}{_color.B:X2}");
            _suppressHexPushback = false;
            UpdateVisuals();
        }

        // ── Palette ──
        private void PaletteTile_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is string hex && TryParse(hex, out var color))
            {
                _color = color; // the tile's exact colour, not an HSV round trip of it
                (_hue, _saturation, _value) = ColorToHsv(color);
                PushColor();
            }
        }

        /// <summary>Four systematic rows over ten evenly spread hues: pastel, vivid, dark, then a
        /// grayscale ramp — a deliberate contrast to the jumbled default palettes of the old picker.</summary>
        private static List<PaletteItem> BuildPalette()
        {
            var hues = new[] { 0, 30, 55, 90, 120, 165, 200, 240, 280, 325 };
            var palette = new List<PaletteItem>();

            foreach (var h in hues) palette.Add(new PaletteItem(Hex(HsvToColor(h, 0.4, 1.0))));   // pastel
            foreach (var h in hues) palette.Add(new PaletteItem(Hex(HsvToColor(h, 1.0, 1.0))));   // vivid
            foreach (var h in hues) palette.Add(new PaletteItem(Hex(HsvToColor(h, 1.0, 0.55))));  // dark

            for (int i = 0; i <= 9; i++)
            {
                byte v = (byte)(i * 255.0 / 9);
                palette.Add(new PaletteItem(Hex(Color.FromRgb(v, v, v))));
            }
            return palette;
        }

        // ── Spectrum square (saturation = x, brightness = y) ──
        private void Spectrum_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _spectrumDragging = true;
            SpectrumCanvas.CaptureMouse();
            UpdateSpectrumFromPoint(e.GetPosition(SpectrumCanvas));
        }

        private void Spectrum_MouseMove(object sender, MouseEventArgs e)
        {
            if (_spectrumDragging) UpdateSpectrumFromPoint(e.GetPosition(SpectrumCanvas));
        }

        private void Spectrum_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _spectrumDragging = false;
            SpectrumCanvas.ReleaseMouseCapture();
        }

        private void Spectrum_TouchDown(object sender, TouchEventArgs e)
        {
            _spectrumDragging = true;
            SpectrumCanvas.CaptureTouch(e.TouchDevice);
            UpdateSpectrumFromPoint(e.GetTouchPoint(SpectrumCanvas).Position);
            e.Handled = true;
        }

        private void Spectrum_TouchMove(object sender, TouchEventArgs e)
        {
            if (!_spectrumDragging) return;
            UpdateSpectrumFromPoint(e.GetTouchPoint(SpectrumCanvas).Position);
            e.Handled = true;
        }

        private void Spectrum_TouchUp(object sender, TouchEventArgs e)
        {
            _spectrumDragging = false;
            SpectrumCanvas.ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
        }

        private void UpdateSpectrumFromPoint(Point p)
        {
            _saturation = Math.Clamp(p.X / SpectrumWidth, 0, 1);
            _value = Math.Clamp(1.0 - p.Y / SpectrumHeight, 0, 1);
            _color = HsvToColor(_hue, _saturation, _value);
            PushColor();
        }

        // ── Hue bar ──
        private void Hue_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _hueDragging = true;
            HueBarGrid.CaptureMouse();
            UpdateHueFromPoint(e.GetPosition(HueBarGrid));
        }

        private void Hue_MouseMove(object sender, MouseEventArgs e)
        {
            if (_hueDragging) UpdateHueFromPoint(e.GetPosition(HueBarGrid));
        }

        private void Hue_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _hueDragging = false;
            HueBarGrid.ReleaseMouseCapture();
        }

        private void Hue_TouchDown(object sender, TouchEventArgs e)
        {
            _hueDragging = true;
            HueBarGrid.CaptureTouch(e.TouchDevice);
            UpdateHueFromPoint(e.GetTouchPoint(HueBarGrid).Position);
            e.Handled = true;
        }

        private void Hue_TouchMove(object sender, TouchEventArgs e)
        {
            if (!_hueDragging) return;
            UpdateHueFromPoint(e.GetTouchPoint(HueBarGrid).Position);
            e.Handled = true;
        }

        private void Hue_TouchUp(object sender, TouchEventArgs e)
        {
            _hueDragging = false;
            HueBarGrid.ReleaseTouchCapture(e.TouchDevice);
            e.Handled = true;
        }

        private void UpdateHueFromPoint(Point p)
        {
            double width = HueBarGrid.ActualWidth > 0 ? HueBarGrid.ActualWidth : SpectrumWidth;
            _hue = Math.Clamp(p.X / width, 0, 1) * 360.0;
            _color = HsvToColor(_hue, _saturation, _value);
            PushColor();
        }

        // ── Hex input ──
        private void HexInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_updatingHexBox) return;

            var text = HexInput.Text.Trim();
            if (text.Length == 7 && TryParse(text, out var color))
            {
                _color = color;
                (_hue, _saturation, _value) = ColorToHsv(color);

                _suppressHexPushback = true;
                SetCurrentValue(SelectedHexProperty, $"#FF{color.R:X2}{color.G:X2}{color.B:X2}");
                _suppressHexPushback = false;

                UpdateVisuals(updateHexBox: false); // never rewrite the box mid-typing
                HexInput.ClearValue(ForegroundProperty);
            }
            else
            {
                // Incomplete or invalid input: flag it, keep the last valid colour.
                HexInput.Foreground = Brushes.Red;
            }
        }

        // ── Visuals ──
        private void UpdateVisuals(bool updateHexBox = true)
        {
            // Guard against callbacks firing while the XAML is still being parsed (e.g. a
            // TextChanged raised by an initial Text attribute, or an early DP change): named
            // elements declared later in the file would still be null then. The Loaded handler
            // repaints once everything exists.
            if (!IsInitialized) return;

            var hex = Hex(_color);

            SpectrumHueBrush.Color = HsvToColor(_hue, 1, 1);
            PreviewBrush.Color = _color;

            Canvas.SetLeft(SpectrumThumb, _saturation * SpectrumWidth - SpectrumThumb.Width / 2);
            Canvas.SetTop(SpectrumThumb, (1.0 - _value) * SpectrumHeight - SpectrumThumb.Height / 2);

            double hueWidth = HueBarGrid.ActualWidth > 0 ? HueBarGrid.ActualWidth : SpectrumWidth;
            Canvas.SetLeft(HueThumb, _hue / 360.0 * hueWidth - HueThumb.Width / 2);

            foreach (var item in _palette)
                item.IsSelected = string.Equals(item.Hex, hex, StringComparison.OrdinalIgnoreCase);

            if (updateHexBox)
            {
                _updatingHexBox = true;
                HexInput.Text = hex;
                HexInput.ClearValue(ForegroundProperty);
                _updatingHexBox = false;
            }
        }

        // ── Colour math ──
        private static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static bool TryParse(string text, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            try
            {
                color = (Color)ColorConverter.ConvertFromString(text.Trim());
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static Color HsvToColor(double h, double s, double v)
        {
            h %= 360;
            if (h < 0) h += 360;
            double c = v * s;
            double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
            double m = v - c;

            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            // Round — truncating drifts channels by 1 (e.g. 0x7F → 0x7E) on HSV round trips.
            return Color.FromRgb(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }

        private static (double H, double S, double V) ColorToHsv(Color c)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double h = 0;
            if (delta > 0)
            {
                if (max == r) h = 60 * ((g - b) / delta % 6);
                else if (max == g) h = 60 * ((b - r) / delta + 2);
                else h = 60 * ((r - g) / delta + 4);
            }
            if (h < 0) h += 360;

            return (h, max > 0 ? delta / max : 0, max);
        }

        /// <summary>One palette tile; IsSelected marks the tile matching the current colour.</summary>
        private sealed class PaletteItem : INotifyPropertyChanged
        {
            public PaletteItem(string hex) => Hex = hex;

            public string Hex { get; }

            private bool _isSelected;
            public bool IsSelected
            {
                get => _isSelected;
                set
                {
                    if (_isSelected == value) return;
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
