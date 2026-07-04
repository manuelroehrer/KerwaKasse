using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using KerwaKasse.Helper;
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

        // ── Direct position input ──

        // Focus once the box becomes visible; calling Focus() during the visibility change is a no-op.
        private void PositionInputBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true && sender is TextBox box)
                Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }), DispatcherPriority.Input);
        }

        private void PositionInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not SavedAnalysisManagementViewModel vm) return;
            if (e.Key == Key.Enter) { vm.CommitPositionEdit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { vm.CancelPositionEdit(); e.Handled = true; }
            else if (e.Key == Key.Space) { e.Handled = true; } // space is not text input, block it here
        }

        // Digits only, and only while the resulting number stays within 1..N.
        private void PositionInputBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (DataContext is SavedAnalysisManagementViewModel vm && sender is TextBox box)
                e.Handled = !NumericTextInput.IsValidPositionTyping(box, e.Text, vm.Items.Count);
        }

        private void PositionInputBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is SavedAnalysisManagementViewModel vm) vm.CommitPositionEdit();
        }

        // Clicking empty space keeps WPF keyboard focus where it is, so a control's own LostFocus
        // commit (position input, but also e.g. the analysis name field, the product search box or
        // a DatePicker) never fires there; see FocusCommitBehavior.
        private void Root_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => FocusCommitBehavior.CommitPendingEditOnOutsideClick(e);
    }
}
