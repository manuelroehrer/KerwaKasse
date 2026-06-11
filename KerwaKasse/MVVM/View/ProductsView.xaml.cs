using KerwaKasse.MVVM.ViewModel;
using ModernWpf.Controls;
using ModernWpf.Controls.Primitives;
using System.Windows;
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

        // Tapping a card in the Speisekarten flyout applies it (with the confirmation in the VM)
        // and closes the flyout. A ContentDialog cannot open while the flyout is up, so close first.
        private void MenuOption_Click(object sender, RoutedEventArgs e)
        {
            var menu = (sender as FrameworkElement)?.DataContext is MenuOptionViewModel option ? option.Menu : null;
            CloseMenuFlyout();
            if (menu != null && DataContext is ProductsViewModel vm)
                vm.ApplyMenuCommand.Execute(menu);
        }

        private void ManageMenus_Click(object sender, RoutedEventArgs e)
        {
            CloseMenuFlyout();
            if (DataContext is ProductsViewModel vm)
                vm.OpenMenuManagementCommand.Execute(null);
        }

        private void CloseMenuFlyout()
            => (FlyoutService.GetFlyout(MenuButton) as FlyoutBase)?.Hide();

        // Give the pill an accent border while its flyout is open (mirrors a focused ModernWpf control).
        private void MenuFlyout_Opened(object sender, object e) => MenuButton.Tag = "open";
        private void MenuFlyout_Closed(object sender, object e) => MenuButton.Tag = null;
    }
}
