using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;
using System;

namespace KerwaKasse.MVVM.ViewModel
{
    public class EditOrderDialogViewModel : PropertyChangedBase
    {
        public RelayCommand SaveCommand { get; }
        public RelayCommand DiscardCommand { get; }
        public RelayCommand NumberBoxChangedCommand { get; }

        public Action CloseDialog { get; set; }

        private OrderModel order;
        public OrderModel Order
        {
            get { return order; }
            set
            {
                order = value;
                OnPropertyChanged();
            }
        }

        public EditOrderDialogViewModel(OrderModel order, Action<OrderModel> onSave)
        {
            Order = order;

            SaveCommand = new RelayCommand(o =>
            {
                onSave(Order);
                CloseDialog?.Invoke();
            });

            DiscardCommand = new RelayCommand(o => CloseDialog?.Invoke());

            NumberBoxChangedCommand = new RelayCommand(o => Order.UpdateDescAndTotal());
        }
    }
}
