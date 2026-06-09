using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace KerwaKasse.MVVM.ViewModel
{
    public class OrderPanelViewModel : PropertyChangedBase
    {
        public RelayCommand AddOrderPositionCommand { get; }
        public RelayCommand DiscardOrderCommand { get; }
        public RelayCommand SaveOrderCommand { get; }

        private readonly IProductService _productService;
        private readonly IOrderService _orderService;
        private readonly ISettingsService _settings;
        private readonly IDialogService _dialogService;

        private List<ProductModel> products;
        public List<ProductModel> Products
        {
            get { return products; }
            set { products = value; OnPropertyChanged(); }
        }

        private ObservableCollection<OrderPositionModel> orderPositions;
        public ObservableCollection<OrderPositionModel> OrderPositions
        {
            get { return orderPositions; }
            set { orderPositions = value; OnPropertyChanged(); }
        }

        private double orderPanelWidth;
        public double OrderPanelWidth
        {
            get { return orderPanelWidth; }
            set { orderPanelWidth = value; OnPropertyChanged(); }
        }

        private double resizeFactorButtons;
        public double Button_ResizeFactor
        {
            get { return resizeFactorButtons; }
            set
            {
                resizeFactorButtons = value;
                OnPropertyChanged();
            }
        }

        private double borderDarkenFactor;
        public double BorderDarkenFactor
        {
            get { return borderDarkenFactor; }
            set { borderDarkenFactor = value; OnPropertyChanged(); }
        }

        private bool useColoredBorder;
        public bool UseColoredBorder
        {
            get { return useColoredBorder; }
            set { useColoredBorder = value; OnPropertyChanged(); }
        }

        private bool showNotification;
        public bool ShowNotification
        {
            get { return showNotification; }
            set { showNotification = value; OnPropertyChanged(); }
        }

        private decimal total;
        public decimal Total
        {
            get { return total; }
            set { total = value; OnPropertyChanged(); }
        }

        public OrderPanelViewModel(IProductService productService, IOrderService orderService, ISettingsService settings, IDialogService dialogService)
        {
            _productService = productService;
            _orderService = orderService;
            _settings = settings;
            _dialogService = dialogService;

            AddOrderPositionCommand = new RelayCommand(o => AddOrderPosition(o as ProductModel));
            DiscardOrderCommand = new RelayCommand(o => DiscardOrder());
            SaveOrderCommand = new RelayCommand(o => SaveOrder());

            LoadPreferences();

            ShowNotification = false;
            Total = 0.0m;
            Products = new List<ProductModel>();
            OrderPositions = new ObservableCollection<OrderPositionModel>();
            LoadData();
        }

        private void LoadPreferences()
        {
            // Write defaults only for keys not yet present (so manual edits are preserved)
            bool needsSave = false;
            needsSave |= _settings.SetIfAbsent("orderPanelCardScaleFactor", 1.0);
            needsSave |= _settings.SetIfAbsent("orderPanelUseColoredBorder", true);
            needsSave |= _settings.SetIfAbsent("orderPanelBorderDarkenFactor", 0.7);
            needsSave |= _settings.SetIfAbsent("orderPanelWidth", 350.0);
            if (needsSave) _settings.Save();

            Button_ResizeFactor = _settings.Get("orderPanelCardScaleFactor", 1.0);
            UseColoredBorder = _settings.Get("orderPanelUseColoredBorder", true);
            BorderDarkenFactor = _settings.Get("orderPanelBorderDarkenFactor", 0.7);
            OrderPanelWidth = _settings.Get("orderPanelWidth", 350.0);
        }

        public void AddOrderPosition(ProductModel product)
        {
            bool productAlreadyOrdered = false;

            foreach (OrderPositionModel order in OrderPositions)
            {
                if (order.Product.ProductID == product.ProductID)
                {
                    productAlreadyOrdered = true;
                    order.Amount++;
                }
            }

            if (!productAlreadyOrdered)
            {
                OrderPositions.Add(new OrderPositionModel() { Product = product });
            }

            decimal total = 0.0m;
            foreach (OrderPositionModel order in OrderPositions)
            {
                total += order.Amount * order.Product.Price;
            }
            Total = total;
        }

        private void LoadData()
        {
            var coreProducts = _productService.GetAvailable();
            Products = coreProducts.Select(p => new ProductModel
            {
                ProductID = p.Id,
                Name = p.Name,
                Price = p.Price,
                Available = p.Available,
                ColorAsString = p.Color,
                PositionNumber = p.SortOrder
            }).OrderBy(o => o.PositionNumber).ToList();
        }

        public void DiscardOrder()
        {
            Total = 0.0m;
            OrderPositions.Clear();
        }

        public void SaveOrder()
        {
            if (OrderPositions.Count == 0)
                return;

            try
            {
                var positions = OrderPositions.Select(op => new OrderPosition
                {
                    ProductId = op.Product.ProductID,
                    Amount = op.Amount,
                    UnitPrice = op.Product.Price
                });
                _orderService.PlaceOrder(positions);
                Notification();
                DiscardOrder();
            }
            catch (Exception e)
            {
                _dialogService.ShowError(e.Message);
            }
        }

        private async void Notification()
        {
            ShowNotification = true;
            await Task.Delay(TimeSpan.FromSeconds(4));
            ShowNotification = false;
        }

        public void SaveOrderPanelWidth(double actualWidth)
        {
            _settings.Set("orderPanelWidth", actualWidth);
            _settings.Save();
        }

        public void SaveButtonResizeFactor()
        {
            _settings.Set("orderPanelCardScaleFactor", Button_ResizeFactor);
            _settings.Save();
        }
    }
}
