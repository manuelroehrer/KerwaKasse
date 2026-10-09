using ModernWpf.Controls;
using System.Windows;
using System.Windows.Input;

namespace KerwaKasse.MVVM.View
{
    /// <summary>Asks for the price of a product with a variable price, starting from its suggested price.
    /// <see cref="Result"/> holds the entered price, or null when the dialog was closed without it.</summary>
    public partial class PriceEntryDialog : ContentDialog
    {
        public decimal? Result { get; private set; }

        public PriceEntryDialog(string productName, decimal suggestion)
        {
            InitializeComponent();
            TitleText.Text = productName;
            Pad.SetSuggestion(suggestion);
            Pad.ValueChanged += (_, _) => ErrorText.Visibility = Visibility.Collapsed;

            // Keyboard focus into the dialog, so a physical keyboard types into the pad right away.
            Opened += (_, _) => Pad.Focus();
            PreviewTextInput += (_, e) => e.Handled = Pad.HandleTextInput(e.Text);
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Back:
                    Pad.HandleBackspace();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    Accept();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Hide();
                    e.Handled = true;
                    break;
            }
        }

        private void Add_Click(object sender, RoutedEventArgs e) => Accept();

        private void Close_Click(object sender, RoutedEventArgs e) => Hide();

        private void Accept()
        {
            if (Pad.Value <= 0)
            {
                ErrorText.Visibility = Visibility.Visible;
                return;
            }

            Result = Pad.Value;
            Hide();
        }
    }
}
