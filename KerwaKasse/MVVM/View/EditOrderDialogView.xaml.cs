using KerwaKasse.MVVM.Model;
using KerwaKasse.MVVM.ViewModel;
using ModernWpf.Controls;
using System;

namespace KerwaKasse.MVVM.View
{
    public partial class EditOrderDialogView : ContentDialog
    {
        public EditOrderDialogView(OrderModel order, Action<OrderModel> onSave)
        {
            InitializeComponent();
            var vm = new EditOrderDialogViewModel(order, onSave);
            vm.CloseDialog = () => Hide();
            DataContext = vm;
        }

        private void NumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            if (DataContext is EditOrderDialogViewModel vm)
                vm.NumberBoxChangedCommand.Execute(null);
        }
    }
}
