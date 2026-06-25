using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KerwaKasse.MVVM.ViewModel;
using ModernWpf.Controls;
using ModernWpf.Controls.Primitives;

namespace KerwaKasse.MVVM.View
{
    public partial class SavedAnalysisManagementDialog : ContentDialog
    {
        // DataContext is assigned by the caller (DialogService).
        public SavedAnalysisManagementDialog()
        {
            InitializeComponent();
            Loaded += (_, _) => AdjustContentHeight();
            SizeChanged += (_, _) => AdjustContentHeight();
        }

        // Cap the content height to the host window so it shrinks on small monitors.
        private void AdjustContentHeight()
        {
            double available = Application.Current?.MainWindow?.ActualHeight ?? SystemParameters.WorkArea.Height;
            RootGrid.MaxHeight = Math.Max(360, available - 72);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Hide();

        // Delete is confirmed via a flyout (a ContentDialog cannot be nested in another).
        private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as SavedAnalysisManagementViewModel)?.DeleteCommand.Execute(null);
            (FlyoutService.GetFlyout(DeleteButton) as FlyoutBase)?.Hide();
        }

        private void CancelDelete_Click(object sender, RoutedEventArgs e)
            => (FlyoutService.GetFlyout(DeleteButton) as FlyoutBase)?.Hide();

        // Clicking empty space in the sidebar list (not a row) clears the selection.
        private void SidebarList_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBox list && e.OriginalSource is DependencyObject source
                && ItemsControl.ContainerFromElement(list, source) == null
                && DataContext is SavedAnalysisManagementViewModel vm)
            {
                vm.Selected = null;
            }
        }

        // Right-clicking a row selects it first, so the per-item context menu (duplicate/delete)
        // acts on that analysis.
        private void AnalysisItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBoxItem item) item.IsSelected = true;
        }
    }
}
