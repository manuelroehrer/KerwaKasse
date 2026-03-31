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
    public class HomeViewModel : PropertyChangedBase
    {
        public RelayCommand AddOrderPositionCommand { get; }
        public RelayCommand DiscardOrderCommand { get; }
        public RelayCommand SaveOrderCommand { get; }
        public RelayCommand SaveButtonResizeFactorCommand { get; }

        private readonly IProductService _productService;
        private readonly IOrderService _orderService;
        private readonly ISettingsService _settings;
        private readonly IDialogService _dialogService;

        private List<ProductModel> products;
        public List<ProductModel> Products
        {
            get { return products; }
            set
            {
                products = value;
                OnPropertyChanged();
            }
        }

        private ObservableCollection<OrderPositionModel> orderPositions;
        public ObservableCollection<OrderPositionModel> OrderPositions
        {
            get { return orderPositions; }
            set
            {
                orderPositions = value;
                OnPropertyChanged();
            }
        }

        private double orderPanelWidth;
        public double OrderPanelWidth
        {
            get { return orderPanelWidth; }
            set
            {
                orderPanelWidth = value;
                OnPropertyChanged();
            }
        }

        private double resizeFactorButtons;
        public double Button_ResizeFactor
        {
            get { return resizeFactorButtons; }
            set
            {
                resizeFactorButtons = value;
                OnPropertyChanged();
                ResizeButtonProperties();
            }
        }

        private double buttons_defaultHeight;
        private double buttons_Height;
        public double Buttons_Height
        {
            get { return buttons_Height; }
            set
            {
                buttons_Height = value;
                OnPropertyChanged();
            }
        }

        private double buttons_defaultWidth;
        private double buttons_Width;
        public double Buttons_Width
        {
            get { return buttons_Width; }
            set
            {
                buttons_Width = value;
                OnPropertyChanged();
            }
        }

        private double buttons_defaultFontSizeBody;
        private double buttons_FontSizeBody;
        public double Buttons_FontSizeBody
        {
            get { return buttons_FontSizeBody; }
            set
            {
                buttons_FontSizeBody = value;
                OnPropertyChanged();
            }
        }

        private double buttons_defaultFontSizeHead;
        private double buttons_FontSizeHead;
        public double Buttons_FontSizeHead
        {
            get { return buttons_FontSizeHead; }
            set
            {
                buttons_FontSizeHead = value;
                OnPropertyChanged();
            }
        }

        private double buttons_defaultPadding;
        private double buttons_Padding;
        public double Buttons_Padding
        {
            get { return buttons_Padding; }
            set
            {
                buttons_Padding = value;
                OnPropertyChanged();
            }
        }

        private bool showNotification;
        public bool ShowNotification
        {
            get { return showNotification; }
            set
            {
                showNotification = value;
                OnPropertyChanged();
            }
        }


        private decimal total;
        public decimal Total
        {
            get { return total; }
            set
            {
                total = value;
                OnPropertyChanged();
            }
        }

        private bool settingsEnabled;
        public bool SettingsEnabled
        {
            get { return settingsEnabled; }
            set
            {
                settingsEnabled = value;
                OnPropertyChanged();
            }
        }
        
        public HomeViewModel(IProductService productService, IOrderService orderService, ISettingsService settings, IDialogService dialogService)
        {
            _productService = productService;
            _orderService = orderService;
            _settings = settings;
            _dialogService = dialogService;

            AddOrderPositionCommand = new RelayCommand(o => AddOrderPosition(o as ProductModel));
            DiscardOrderCommand = new RelayCommand(o => DiscardOrder());
            SaveOrderCommand = new RelayCommand(o => SaveOrder());
            SaveButtonResizeFactorCommand = new RelayCommand(o => SaveButtonResizeFactor());

            SettingsEnabled = false;
            LoadPreferences();
            ResizeButtonProperties();

            ShowNotification = false;
            Total = 0.0m;
            Products = new List<ProductModel>();
            OrderPositions = new ObservableCollection<OrderPositionModel>();
            LoadData();
        }

        private void LoadPreferences()
        {
            Button_ResizeFactor = _settings.Get("buttonResizeFactor", 2.0);
            buttons_defaultHeight = _settings.Get("buttonDefaultHeight", 55.0);
            buttons_defaultWidth = _settings.Get("buttonDefaultWidth", 80.0);
            buttons_defaultFontSizeBody = _settings.Get("buttonsDefaultFontSizeBody", 6.0);
            buttons_defaultFontSizeHead = _settings.Get("buttonsDefaultFontSizeHead", 10.0);
            buttons_defaultPadding = _settings.Get("buttonsDefaultPadding", 4.0);
            OrderPanelWidth = _settings.Get("homeOrderPanelWidth", 350.0);
        }

        /// <summary>
        /// Rezizes the product buttons by the factor setted with the slider in the settings pane
        /// </summary>
        private void ResizeButtonProperties()
        {
            Buttons_Height = buttons_defaultHeight * Button_ResizeFactor;
            Buttons_Width = buttons_defaultWidth * Button_ResizeFactor;
            Buttons_FontSizeBody = buttons_defaultFontSizeBody * Button_ResizeFactor;
            Buttons_FontSizeHead = buttons_defaultFontSizeHead * Button_ResizeFactor;
            Buttons_Padding = buttons_defaultPadding * Button_ResizeFactor;
        }

        /// <summary>
        /// Adds a new order position to the list of order positions.
        /// If the product is already ordered, increments the amount by 1.
        /// If the product is not already ordered, adds a new order position with the specified product.
        /// Recalculates the total amount for the order.
        /// </summary>
        /// <param name="product">The product to be added to the order.</param>
        public void AddOrderPosition(ProductModel product)
        {
            bool productAlreadyOrdered = false;

            // Iterate through the list of existing order positions to check if the product has already been ordered. If so, the amount gets increased by one
            foreach (OrderPositionModel order in OrderPositions)
            {
                if(order.Product.ProductID == product.ProductID)
                {
                    productAlreadyOrdered = true;
                    order.Amount++;
                }
            }

            // If the product has not been ordered yet, add the new product as order position to list
            if (productAlreadyOrdered == false)
            {
                OrderPositions.Add(new OrderPositionModel() { Product = product });                
            }

            // Update the total amount for the order
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
                Description = p.Description,
                Price = p.Price,
                Available = p.Available,
                ColorAsString = p.Color,
                ImgPath = p.ImagePath,
                PositionNumber = p.SortOrder
            }).OrderBy(o => o.PositionNumber).ToList();
        }

        /// <summary>
        /// Discards the current order by resetting the total amount to 0.0 and clearing the list of order positions.
        /// </summary>
        public void DiscardOrder()
        {
            Total = 0.0m;
            OrderPositions.Clear();
        }

        /// <summary>
        /// Saves the current order to database, triggers a notification, and resets the current order view
        /// </summary>
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
                    UnitPrice = op.Product.Price,
                    ProductDescription = op.Product.Description
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
            _settings.Set("homeOrderPanelWidth", actualWidth);
            _settings.Save();
        }

        public void SaveButtonResizeFactor()
        {
            _settings.Set("buttonResizeFactor", Button_ResizeFactor);
            _settings.Save();
        }
    }
}
