using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace KerwaKasse.MVVM.ViewModel
{
    public class HistoryViewModel : PropertyChangedBase
    {
        public RelayCommand EditOrderCommand { get; }

        public Action<OrderModel, Action<OrderModel>> ShowEditOrderDialog { get; set; }

        private readonly IOrderService _orderService;

        private ObservableCollection<OrderModel> orders;
        public ObservableCollection<OrderModel> Orders
        {
            get { return orders; }
            set
            {
                orders = value;
                OnPropertyChanged();
            }
        }

        private DateTime date;
        public DateTime Date
        {
            get { return date; }
            set
            {
                date = value;
                OnPropertyChanged();
                LoadData();
            }
        }

        public HistoryViewModel(IOrderService orderService)
        {
            _orderService = orderService;

            EditOrderCommand = new RelayCommand(o =>
            {
                if (o is OrderModel order)
                    EditOrder(order);
            });

            Orders = new ObservableCollection<OrderModel>();
            Date = DateTime.Today;
        }

        public void Reload()
        {
            LoadData();
        }

        private void EditOrder(OrderModel order)
        {
            ShowEditOrderDialog?.Invoke(order, SaveOrderChanges);
            LoadData();
        }

        private void SaveOrderChanges(OrderModel order)
        {
            var coreOrder = new Order
            {
                Id = order.OrderID,
                OrderTime = order.OrderTime,
                Positions = order.OrderPositions.Select(op => new OrderPosition
                {
                    OrderId = order.OrderID,
                    ProductId = op.Product.ProductID,
                    Amount = op.Amount,
                    UnitPrice = op.Product.Price,
                    ProductDescription = op.Product.Description
                }).ToList()
            };
            _orderService.UpdateOrderPositions(coreOrder);
        }

        private void LoadData()
        {
            Orders.Clear();
            var coreOrders = _orderService.GetOrdersByDate(Date);

            if (coreOrders != null && coreOrders.Count > 0)
            {
                var mapped = coreOrders.Select(o =>
                {
                    var om = new OrderModel
                    {
                        OrderID = o.Id,
                        OrderTime = o.OrderTime,
                        OrderPositions = o.Positions.Select(p => new OrderPositionModel
                        {
                            Amount = p.Amount,
                            Product = new ProductModel
                            {
                                ProductID = p.ProductId,
                                Description = p.ProductDescription,
                                Price = p.UnitPrice
                            }
                        }).ToList()
                    };
                    om.UpdateDescAndTotal();
                    return om;
                });
                Orders = new ObservableCollection<OrderModel>(mapped.OrderByDescending(o => o.OrderID));
            }
        }
    }
}
