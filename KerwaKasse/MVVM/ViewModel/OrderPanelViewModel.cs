using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;
using Microsoft.Extensions.Logging;
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
        private readonly ILogger<OrderPanelViewModel> _logger;

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
        /// <summary>The manual-slider value. Only ever changed by the user dragging that slider (or
        /// loading it from settings) — auto-fit never writes here, so it always reflects the user's
        /// own last manual choice. <see cref="EffectiveScaleFactor"/> is what actually gets applied
        /// to the tiles.</summary>
        public double Button_ResizeFactor
        {
            get { return resizeFactorButtons; }
            set
            {
                resizeFactorButtons = value;
                OnPropertyChanged();
                if (!_autoFitTiles) EffectiveScaleFactor = value;
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

        public OrderPanelViewModel(IProductService productService, IOrderService orderService, ISettingsService settings, IDialogService dialogService, ILogger<OrderPanelViewModel> logger)
        {
            _productService = productService;
            _orderService = orderService;
            _settings = settings;
            _dialogService = dialogService;
            _logger = logger;

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
            // Auto-fit is the default for fresh installs and for updates where the key is new.
            needsSave |= _settings.SetIfAbsent("orderPanelAutoFitTiles", true);
            needsSave |= _settings.SetIfAbsent("orderPanelAutoFitMaxScale", 2.5);
            if (needsSave) _settings.Save();

            Button_ResizeFactor = _settings.Get("orderPanelCardScaleFactor", 1.0);
            UseColoredBorder = _settings.Get("orderPanelUseColoredBorder", true);
            BorderDarkenFactor = _settings.Get("orderPanelBorderDarkenFactor", 0.7);
            OrderPanelWidth = _settings.Get("orderPanelWidth", 350.0);

            // Fields on purpose: reading the stored values must not re-save or recompute.
            _autoFitTiles = _settings.Get("orderPanelAutoFitTiles", true);
            _autoFitMaxFactor = _settings.Get("orderPanelAutoFitMaxScale", 2.5);
        }

        // ── Tile auto-fit (optional mode; the slider stays as the manual alternative) ──

        private bool _autoFitTiles;
        public bool AutoFitTiles
        {
            get => _autoFitTiles;
            set
            {
                if (_autoFitTiles == value) return;
                _autoFitTiles = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsManualTileSize));
                _settings.Set("orderPanelAutoFitTiles", value);
                _settings.Save();

                if (value)
                    RecomputeAutoFit();
                else
                    // Hand control back to the manual slider's own (untouched) value.
                    EffectiveScaleFactor = Button_ResizeFactor;
            }
        }

        /// <summary>Drives the slider's enabled state (manual sizing only while auto-fit is off).</summary>
        public bool IsManualTileSize => !_autoFitTiles;

        private double _effectiveScaleFactor = 1.0;
        /// <summary>The scale actually applied to the product tiles: mirrors
        /// <see cref="Button_ResizeFactor"/> while auto-fit is off, or the computed auto-fit factor
        /// while it is on. Kept separate from Button_ResizeFactor so adjusting
        /// <see cref="AutoFitMaxFactor"/> never moves the (disabled) manual slider.</summary>
        public double EffectiveScaleFactor
        {
            get => _effectiveScaleFactor;
            private set
            {
                if (Math.Abs(_effectiveScaleFactor - value) < 0.0005) return;
                _effectiveScaleFactor = value;
                OnPropertyChanged();
            }
        }

        // Upper bound for the computed factor, so a day with only a few products does not blow the
        // tiles up to comical sizes. Adjustable via its own slider while auto-fit is on.
        private double _autoFitMaxFactor = 2.5;
        public double AutoFitMaxFactor
        {
            get => _autoFitMaxFactor;
            set
            {
                if (Math.Abs(_autoFitMaxFactor - value) < 0.001) return;
                _autoFitMaxFactor = value;
                OnPropertyChanged();
                RecomputeAutoFit();
            }
        }

        /// <summary>Persists the max-scale slider value (called by the view on drag end, like the
        /// other sliders, so dragging does not write the file for every tick).</summary>
        public void SaveAutoFitMaxFactor()
        {
            _settings.Set("orderPanelAutoFitMaxScale", AutoFitMaxFactor);
            _settings.Save();
        }

        private double _autoFitAreaWidth;
        private double _autoFitAreaHeight;

        /// <summary>Called by the view whenever the product tile area resizes.</summary>
        public void UpdateAutoFitArea(double width, double height)
        {
            _autoFitAreaWidth = width;
            _autoFitAreaHeight = height;
            RecomputeAutoFit();
        }

        private void RecomputeAutoFit()
        {
            if (!AutoFitTiles || Products == null || Products.Count == 0) return;
            if (_autoFitAreaWidth <= 0 || _autoFitAreaHeight <= 0) return;
            EffectiveScaleFactor = ComputeAutoFitFactor(_autoFitAreaWidth, _autoFitAreaHeight, Products.Count, _autoFitMaxFactor);
        }

        /// <summary>The largest scale factor at which all tiles fit the area without scrolling:
        /// for every possible column count, the factor is limited by the row width and by the
        /// resulting number of rows; the best combination wins. Clamped to [0.5, maxFactor].</summary>
        public static double ComputeAutoFitFactor(double areaWidth, double areaHeight, int tileCount, double maxFactor)
        {
            // Tile footprint at factor 1.0: the 170×105 card plus its uniform 4px margin on every
            // side (see OrderPanelView).
            const double TileFootprintWidth = 178;
            const double TileFootprintHeight = 113;
            const double MinFactor = 0.5;

            if (tileCount <= 0 || areaWidth <= 0 || areaHeight <= 0) return 1.0;

            double best = MinFactor;
            for (int columns = 1; columns <= tileCount; columns++)
            {
                int rows = (int)Math.Ceiling((double)tileCount / columns);
                double factor = Math.Min(areaWidth / (columns * TileFootprintWidth),
                                         areaHeight / (rows * TileFootprintHeight));
                best = Math.Max(best, factor);
            }

            return Math.Clamp(best, MinFactor, Math.Max(MinFactor, maxFactor));
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
                _logger.LogError(e, "Failed to place order ({PositionCount} positions, total {Total:0.00} €)",
                    OrderPositions.Count, Total);
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
