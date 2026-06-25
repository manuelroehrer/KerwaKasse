using KerwaKasse.MVVM.Model;
using ModernWpf.Controls;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Input;

namespace KerwaKasse.MVVM.View
{
    public partial class AddPositionDialog : ContentDialog
    {
        public ProductModel SelectedProduct => ProductList.SelectedItem as ProductModel;

        /// <summary>True when the user confirmed via double-click (which closes with result None).</summary>
        public bool Confirmed { get; private set; }

        public AddPositionDialog(IEnumerable<ProductModel> products)
        {
            InitializeComponent();
            ProductList.ItemsSource = products;
        }

        private void ProductList_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => IsPrimaryButtonEnabled = ProductList.SelectedItem != null;

        // Double-clicking a product confirms the dialog immediately.
        private void ProductList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ProductList.SelectedItem != null)
            {
                Confirmed = true;
                Hide();
            }
        }
    }
}
