using KerwaKasse.MVVM.Model;
using KerwaKasse.MVVM.ViewModel;
using ModernWpf.Controls;
using System;
using System.Windows;
using System.Windows.Controls;

namespace KerwaKasse.MVVM.View
{
    public partial class HistoryView : UserControl
    {
        public HistoryView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is HistoryViewModel vm)
                vm.ShowEditOrderDialog = ShowEditDialog;
        }

        private async void ShowEditDialog(OrderModel order, Action<OrderModel> onSave)
        {
            var dialog = new EditOrderDialogView(order, onSave);
            await dialog.ShowAsync(ContentDialogPlacement.Popup);
            if (DataContext is HistoryViewModel vm)
                vm.Reload();
        }
    }
}
