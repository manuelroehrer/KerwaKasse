using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KerwaKasse.MVVM.ViewModel;
using ModernWpf.Controls;
using ModernWpf.Controls.Primitives;

namespace KerwaKasse.MVVM.View
{
    public partial class MenuManagementDialog : ContentDialog
    {
        // DataContext is assigned by the caller (DialogService) so the dialog
        // stays free of service dependencies.
        public MenuManagementDialog()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

        // Delete is confirmed via a flyout (a ContentDialog cannot be nested in another).
        private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as MenuManagementViewModel)?.DeleteMenuCommand.Execute(null);
            (FlyoutService.GetFlyout(DeleteButton) as FlyoutBase)?.Hide();
        }

        private void CancelDelete_Click(object sender, RoutedEventArgs e)
            => (FlyoutService.GetFlyout(DeleteButton) as FlyoutBase)?.Hide();

        // Clicking the header clears the current selection (back to the "no card selected" state).
        private void DeselectArea_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is MenuManagementViewModel vm)
                vm.SelectedMenu = null;
        }

        // Clicking empty space in the sidebar list (not a row) also clears the selection.
        private void SidebarList_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBox list && e.OriginalSource is DependencyObject source
                && ItemsControl.ContainerFromElement(list, source) == null
                && DataContext is MenuManagementViewModel vm)
            {
                vm.SelectedMenu = null;
            }
        }
    }
}
