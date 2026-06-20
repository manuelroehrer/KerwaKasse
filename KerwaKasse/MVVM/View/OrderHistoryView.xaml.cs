using KerwaKasse.MVVM.Model;
using KerwaKasse.MVVM.ViewModel;
using ModernWpf.Controls;
using ModernWpf.Controls.Primitives;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KerwaKasse.MVVM.View
{
    public partial class OrderHistoryView : UserControl
    {
        private OrderHistoryViewModel _vm;
        private int _validationErrors;

        // Detail panel's preferred width + the gap; the list column only grows once the panel is back
        // to this width.
        private const double PanelPreferredWidth = 720;
        private const double ColumnGap = 16;
        private const double MasterMinWidth = 360;
        private const double MasterMaxWidth = 560;
        private const double SearchExpandedWidth = 218;
        private const int SearchAnimationMilliseconds = 150;

        public OrderHistoryView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            // Bubbled validation errors from the amount steppers gate the save button.
            AddHandler(Validation.ErrorEvent, new RoutedEventHandler(OnValidationError));
            OuterScroll.SizeChanged += (_, _) => UpdateMasterColumnWidth();
            Loaded += (_, _) => UpdateMasterColumnWidth();
        }

        // Give surplus width to the detail panel first: keep the list at its minimum until the panel
        // has regained its preferred width, then let the list grow (up to its maximum). Uses
        // ActualWidth (reliable across maximize/restore) rather than ViewportWidth.
        private void UpdateMasterColumnWidth()
        {
            double available = OuterScroll.ActualWidth - ColumnGap - PanelPreferredWidth;
            double width = Math.Max(MasterMinWidth, Math.Min(MasterMaxWidth, available));
            MasterColumn.Width = new GridLength(width);
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null)
                _vm.PropertyChanged -= Vm_PropertyChanged;

            if (e.NewValue is OrderHistoryViewModel vm)
            {
                _vm = vm;
                vm.ShowAddPositionPicker = ShowAddPositionPicker;
                vm.PropertyChanged += Vm_PropertyChanged;
            }
        }

        // Entering or leaving edit mode rebuilds the steppers, so reset the error tracking to avoid
        // a stale error blocking the save button.
        private void Vm_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(OrderHistoryViewModel.IsEditing))
            {
                _validationErrors = 0;
                if (_vm != null)
                    _vm.HasInputError = false;
            }
        }

        private void OnValidationError(object sender, RoutedEventArgs e)
        {
            if (e is not ValidationErrorEventArgs args)
                return;

            if (args.Action == ValidationErrorEventAction.Added)
                _validationErrors++;
            else if (args.Action == ValidationErrorEventAction.Removed)
                _validationErrors--;

            if (_validationErrors < 0)
                _validationErrors = 0;

            if (_vm != null)
                _vm.HasInputError = _validationErrors > 0;
        }

        // Opens the add-product dialog and reports the chosen product back to the view model.
        private async void ShowAddPositionPicker(IReadOnlyList<ProductModel> candidates, Action<ProductModel> onPicked)
        {
            var dialog = new AddPositionDialog(candidates, _vm.BorderDarkenFactor, _vm.UseColoredBorder);
            var result = await dialog.ShowAsync();

            if ((result == ContentDialogResult.Primary || dialog.Confirmed) && dialog.SelectedProduct != null)
                onPicked(dialog.SelectedProduct);
        }

        // Clicking empty space around the detail card clears the selection.
        private void DetailBackground_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is OrderHistoryViewModel vm)
                vm.SelectedOrder = null;
        }

        // Clicking empty space inside the detail card moves focus to the card, releasing the number
        // field's focus (and its accent ring). Buttons/fields handle their own clicks, so this only
        // fires for the white area; the card is a sibling of the deselect catcher, so the selection
        // is kept.
        private void DetailCard_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is IInputElement card)
                card.Focus();
        }

        // Clicking empty space in the list (not a row) also clears the selection.
        private void HistoryList_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is ListBox list && e.OriginalSource is DependencyObject source
                && ItemsControl.ContainerFromElement(list, source) == null
                && DataContext is OrderHistoryViewModel vm)
            {
                vm.SelectedOrder = null;
            }
        }

        // Delete is confirmed via a flyout (consistent with the Speisekarten dialog).
        private void ConfirmDelete_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as OrderHistoryViewModel)?.DeleteOrderCommand.Execute(null);
            (FlyoutService.GetFlyout(DeleteButton) as FlyoutBase)?.Hide();
        }

        private void CancelDelete_Click(object sender, RoutedEventArgs e)
            => (FlyoutService.GetFlyout(DeleteButton) as FlyoutBase)?.Hide();

        private void SearchToggle_Click(object sender, RoutedEventArgs e)
            => ExpandSearch();

        private void SearchClear_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is OrderHistoryViewModel vm)
                vm.SearchText = string.Empty;

            CollapseSearch();
            SearchToggleButton.Focus();
        }

        private void SearchBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (DataContext is OrderHistoryViewModel vm && vm.IsSearchActive)
                return;

            CollapseSearch();
        }

        private void ExpandSearch()
        {
            SearchToggleButton.Visibility = Visibility.Collapsed;
            AnimateSearchWidth(SearchExpandedWidth);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
                SearchBox.SelectAll();
            }), DispatcherPriority.Input);
        }

        private void CollapseSearch()
        {
            if (SearchHost.Width <= 0.5)
            {
                SearchToggleButton.Visibility = Visibility.Visible;
                return;
            }

            AnimateSearchWidth(0);
        }

        private void AnimateSearchWidth(double targetWidth)
        {
            SearchHost.BeginAnimation(WidthProperty, null);
            SearchHost.Width = SearchHost.ActualWidth;

            var animation = new DoubleAnimation
            {
                To = targetWidth,
                Duration = TimeSpan.FromMilliseconds(SearchAnimationMilliseconds),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            animation.Completed += (_, _) =>
            {
                SearchHost.Width = targetWidth;
                if (targetWidth <= 0.5)
                    SearchToggleButton.Visibility = Visibility.Visible;
            };
            SearchHost.BeginAnimation(WidthProperty, animation);
        }
    }
}
