using KerwaKasse.MVVM.ViewModel;
using System.Windows.Controls;

namespace KerwaKasse.MVVM.View
{
    public partial class ProductsView : UserControl
    {
        public ProductsView()
        {
            InitializeComponent();
        }

        private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (DataContext is ProductsViewModel vm && MainGrid.ColumnDefinitions.Count >= 3)
                vm.SaveSidePanelWidth(MainGrid.ColumnDefinitions[2].ActualWidth);
        }
    }
}
