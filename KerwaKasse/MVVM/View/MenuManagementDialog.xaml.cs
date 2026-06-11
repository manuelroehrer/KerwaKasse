using System.Windows;
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
    }
}
