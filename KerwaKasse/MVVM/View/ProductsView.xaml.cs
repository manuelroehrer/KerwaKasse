using KerwaKasse.Helper;
using KerwaKasse.MVVM.ViewModel;
using ModernWpf.Controls;
using ModernWpf.Controls.Primitives;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

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

        // ── Direct position input ──

        // Focus once the box becomes visible; calling Focus() during the visibility change is a no-op.
        private void PositionInputBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true && sender is TextBox box)
                Dispatcher.BeginInvoke(new Action(() => { box.Focus(); box.SelectAll(); }), DispatcherPriority.Input);
        }

        private void PositionInputBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (DataContext is not ProductsViewModel vm) return;
            if (e.Key == Key.Enter) { vm.CommitPositionEdit(); e.Handled = true; }
            else if (e.Key == Key.Escape) { vm.CancelPositionEdit(); e.Handled = true; }
            else if (e.Key == Key.Space) { e.Handled = true; } // space is not text input, block it here
        }

        // Digits only, and only while the resulting number stays within 1..N.
        private void PositionInputBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (DataContext is ProductsViewModel vm && sender is TextBox box)
                e.Handled = !NumericTextInput.IsValidPositionTyping(box, e.Text, vm.Products.Count);
        }

        private void PositionInputBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProductsViewModel vm) vm.CommitPositionEdit();
        }

        // Clicking empty space keeps WPF keyboard focus where it is, so a control's own LostFocus
        // commit (position input, but also e.g. the Name/Price fields or a DatePicker elsewhere)
        // never fires there; see FocusCommitBehavior.
        private void Root_PreviewMouseDown(object sender, MouseButtonEventArgs e)
            => FocusCommitBehavior.CommitPendingEditOnOutsideClick(e);
    }
}
