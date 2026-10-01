using KerwaKasse.MVVM.ViewModel;
using ModernWpf.Controls;
using ModernWpf.Controls.Primitives;
using System.Windows;
using System.Windows.Controls;

namespace KerwaKasse.MVVM.View
{
    public partial class OrderPanelView : UserControl
    {
        public OrderPanelView()
        {
            InitializeComponent();

            // Navigating to the order panel while it is already shown reuses this view
            // (same DataTemplate) and only swaps the DataContext, so no SizeChanged fires
            // for the fresh view model — push the current tile area to it here instead.
            DataContextChanged += (_, _) => PushAutoFitArea();
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

        // A flyout entry: close the flyout first, then switch the tiles into arranging mode.
        private void ArrangeTiles_Click(object sender, RoutedEventArgs e)
        {
            (FlyoutService.GetFlyout(SettingsButton) as FlyoutBase)?.Hide();
            if (DataContext is OrderPanelViewModel vm)
                vm.StartArrangingCommand.Execute(null);
        }

        private void ProductScroll_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            PushAutoFitArea();
        }

        // Feeds the tile area size (viewport minus padding, small safety margin against
        // float rounding) into the view model for the auto-fit computation.
        private void PushAutoFitArea()
        {
            if (DataContext is OrderPanelViewModel vm && ProductScroll.ActualWidth > 0)
                vm.UpdateAutoFitArea(
                    ProductScroll.ActualWidth - ProductScroll.Padding.Left - ProductScroll.Padding.Right - 2,
                    ProductScroll.ActualHeight - ProductScroll.Padding.Top - ProductScroll.Padding.Bottom - 2);
        }
    }
}
