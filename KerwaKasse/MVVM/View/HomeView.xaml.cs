using KerwaKasse.MVVM.ViewModel;
using System.Windows.Controls;

namespace KerwaKasse.MVVM.View
{
    public partial class HomeView : UserControl
    {
        public HomeView()
        {
            InitializeComponent();
        }

        private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (DataContext is HomeViewModel vm)
                vm.SaveOrderPanelWidth(MainGrid.ColumnDefinitions[2].ActualWidth);
        }

        private void Slider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (DataContext is HomeViewModel vm)
                vm.SaveButtonResizeFactor();
        }
    }
}
