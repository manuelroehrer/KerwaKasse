using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;
using ModernWpf.Controls;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KerwaKasse.MVVM.View
{
    /// <summary>"Position nachtragen" in the history: picks one of the products, or "Freie Eingabe".
    /// <see cref="Result"/> is null when the dialog was closed without a choice.</summary>
    public partial class AddPositionDialog : ContentDialog
    {
        public AddPositionChoice Result { get; private set; }

        public AddPositionDialog(IEnumerable<ProductModel> products)
        {
            InitializeComponent();
            var options = new List<Option> { new(null, "Freie Eingabe", OrderPositionModel.FreeEntryBrush) };
            options.AddRange(products.Select(p => new Option(p, p.Name, p.Color)));
            OptionList.ItemsSource = options;

            Opened += (_, _) => OptionList.Focus();
            PreviewKeyDown += OnPreviewKeyDown;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
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

        private void OptionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => AddButton.IsEnabled = OptionList.SelectedItem != null;

        // Double-clicking an entry adds it right away.
        private void OptionList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Accept();

        private void Add_Click(object sender, RoutedEventArgs e) => Accept();

        private void Close_Click(object sender, RoutedEventArgs e) => Hide();

        private void Accept()
        {
            if (OptionList.SelectedItem is not Option option)
                return;

            Result = new AddPositionChoice(option.Product);
            Hide();
        }

        /// <summary>One entry of the list: a product, or "Freie Eingabe" without one.</summary>
        public sealed record Option(ProductModel Product, string Name, Brush Color)
        {
            public bool IsFreeEntry => Product == null;

            // What UI Automation (screen readers) reads for the entry, instead of the record's member dump.
            public override string ToString() => Name;
        }
    }
}
