using KerwaKasse.MVVM.ViewModel;
using System.Windows;
using System.Windows.Controls;

namespace KerwaKasse.MVVM.View
{
    public partial class OrderPanelView : UserControl
    {
        public OrderPanelView()
        {
            InitializeComponent();
        }

        private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (DataContext is OrderPanelViewModel vm)
                vm.SaveOrderPanelWidth(MainGrid.ColumnDefinitions[2].ActualWidth);
        }

        private void Slider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (DataContext is OrderPanelViewModel vm)
                vm.SaveButtonResizeFactor();
        }

        private void MaxFactorSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            if (DataContext is OrderPanelViewModel vm)
                vm.SaveAutoFitMaxFactor();
        }

        // Feeds the tile area size (viewport minus padding, small safety margin against
        // float rounding) into the view model for the auto-fit computation.
        private void ProductScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (DataContext is OrderPanelViewModel vm && sender is ScrollViewer scroll)
                vm.UpdateAutoFitArea(
                    e.NewSize.Width - scroll.Padding.Left - scroll.Padding.Right - 2,
                    e.NewSize.Height - scroll.Padding.Top - scroll.Padding.Bottom - 2);
        }
    }
}
