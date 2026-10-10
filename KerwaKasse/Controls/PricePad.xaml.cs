using KerwaKasse.Helper;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace KerwaKasse.Controls
{
    /// <summary>
    /// Touch number pad for an amount in euros: a display and calculator-style keys (see
    /// <see cref="PriceEntryBuffer"/>). A physical keyboard works too: the hosting dialog forwards its
    /// text input and Backspace (<see cref="HandleTextInput"/>, <see cref="HandleBackspace"/>), so typing
    /// reaches the pad wherever the focus is in the dialog.
    /// </summary>
    public partial class PricePad : UserControl
    {
        private static readonly Brush SuggestionBackground = Frozen(Color.FromRgb(0xE3, 0xED, 0xF8));

        private readonly PriceEntryBuffer _buffer = new();

        public PricePad()
        {
            InitializeComponent();
            Refresh();
        }

        /// <summary>Raised whenever the value changes.</summary>
        public event EventHandler ValueChanged;

        public decimal Value => _buffer.Value;

        /// <summary>Shows <paramref name="price"/> as a suggestion that the first typed key replaces.</summary>
        public void SetSuggestion(decimal price)
        {
            _buffer.SetSuggestion(price);
            Refresh();
        }

        /// <summary>Takes typed text from a physical keyboard; returns whether any of it was used.</summary>
        public bool HandleTextInput(string text)
        {
            bool used = false;
            foreach (char key in text ?? string.Empty)
                used |= _buffer.Type(key);
            if (used) Refresh();
            return used;
        }

        public void HandleBackspace()
        {
            _buffer.Backspace();
            Refresh();
        }

        private void Key_Click(object sender, RoutedEventArgs e)
        {
            string key = (string)((Button)sender).Tag;
            if (key == "back")
                _buffer.Backspace();
            else
                _buffer.Type(key[0]);
            Refresh();
        }

        private void Refresh()
        {
            DisplayText.Text = _buffer.Value.ToString("C", CultureInfo.CurrentCulture);
            DisplayBorder.Background = _buffer.IsSuggestion ? SuggestionBackground : Brushes.White;
            SuggestionHint.Visibility = _buffer.IsSuggestion ? Visibility.Visible : Visibility.Collapsed;
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
