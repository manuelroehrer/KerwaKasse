using System;
using System.Collections.ObjectModel;
using System.Linq;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;

namespace KerwaKasse.MVVM.ViewModel
{
    /// <summary>
    /// Main view model for the product management view.
    ///
    /// The right-side panel has two modes (placeholder / product editing); saving or
    /// discarding returns to the placeholder.
    /// </summary>
    public class ProductsViewModel : PropertyChangedBase
    {
        private readonly IProductService _productService;
        private readonly ISettingsService _settings;
        private readonly IDialogService _dialogService;

        // ── Product list ─────────────────────────────────────────
        public ObservableCollection<ProductListItemViewModel> Products { get; } = new();

        private ProductListItemViewModel _selectedProduct;
        private bool _suppressSelectionChange;

        public ProductListItemViewModel SelectedProduct
        {
            get => _selectedProduct;
            set
            {
                if (_selectedProduct != value)
                {
                    _selectedProduct = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentPositionText));
                    if (!_suppressSelectionChange)
                        OnSelectedProductChanged();
                }
            }
        }

        // ── Detail panel (product editing) ───────────────────────
        private ProductDetailViewModel _detailPanel;
        public ProductDetailViewModel DetailPanel
        {
            get => _detailPanel;
            private set
            {
                _detailPanel = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDetailPanelVisible));
                OnPropertyChanged(nameof(IsPlaceholderVisible));
            }
        }

        private bool _isAddingNew;
        public bool IsAddingNew
        {
            get => _isAddingNew;
            private set
            {
                _isAddingNew = value;
                OnPropertyChanged();
            }
        }

        public bool IsDetailPanelVisible => _detailPanel != null;
        public bool IsPlaceholderVisible => _detailPanel == null;

        /// <summary>"X / N" position of the selected product within the list (shown next to the move buttons).</summary>
        public string CurrentPositionText
        {
            get
            {
                if (_selectedProduct == null) return string.Empty;
                int idx = Products.IndexOf(_selectedProduct);
                return idx >= 0 ? $"{idx + 1} / {Products.Count}" : string.Empty;
            }
        }

        // ── Side-panel width (persisted) ─────────────────────────
        private double _sidePanelWidth;
        public double SidePanelWidth
        {
            get => _sidePanelWidth;
            set
            {
                _sidePanelWidth = value;
                OnPropertyChanged();
            }
        }

        /// <summary>Swatch border preferences (persisted): whether to tint the border and how much to darken it.</summary>
        public double BorderDarkenFactor { get; private set; }
        public bool UseColoredBorder { get; private set; }

        // ── Commands ─────────────────────────────────────────────
        public RelayCommand AddProductCommand { get; }
        public RelayCommand SaveDetailCommand { get; }
        public RelayCommand DiscardDetailCommand { get; }
        public RelayCommand ToggleNameLockCommand { get; }
        public RelayCommand MoveSelectedUpCommand { get; }
        public RelayCommand MoveSelectedDownCommand { get; }

        public ProductsViewModel(
            IProductService productService,
            ISettingsService settings,
            IDialogService dialogService)
        {
            _productService = productService;
            _settings = settings;
            _dialogService = dialogService;

            LoadPreferences();

            AddProductCommand = new RelayCommand(_ => BeginAddProduct());
            SaveDetailCommand = new RelayCommand(_ => SaveDetail(),
                _ => DetailPanel != null && !DetailPanel.HasErrors && (DetailPanel.IsNew || DetailPanel.IsDirty));
            DiscardDetailCommand = new RelayCommand(_ => DiscardDetail(), _ => DetailPanel != null);
            ToggleNameLockCommand = new RelayCommand(_ => ToggleNameLock(), _ => DetailPanel is { IsNew: false });
            MoveSelectedUpCommand = new RelayCommand(_ => MoveSelected(-1), _ => CanMoveSelected(-1));
            MoveSelectedDownCommand = new RelayCommand(_ => MoveSelected(1), _ => CanMoveSelected(1));

            LoadData();
        }

        // ── Preferences ──────────────────────────────────────────

        private void LoadPreferences()
        {
            bool needsSave = false;
            needsSave |= _settings.SetIfAbsent("productsSwatchUseColoredBorder", true);
            needsSave |= _settings.SetIfAbsent("productsSwatchBorderDarkenFactor", 0.7);
            if (needsSave) _settings.Save();

            SidePanelWidth = _settings.Get("productsSidePanelWidth", 440.0);
            UseColoredBorder = _settings.Get("productsSwatchUseColoredBorder", true);
            BorderDarkenFactor = _settings.Get("productsSwatchBorderDarkenFactor", 0.7);
        }

        // ── Data loading ─────────────────────────────────────────

        public void LoadData()
        {
            var selected = SelectedProduct;

            var coreProducts = _productService.GetAll();
            Products.Clear();
            foreach (var p in coreProducts)
            {
                Products.Add(new ProductListItemViewModel(
                    p.Id, p.Description, p.Price, p.Color, p.Available,
                    OnProductAvailabilityToggled));
            }

            if (selected != null)
                SelectedProduct = Products.FirstOrDefault(p => p.ProductId == selected.ProductId);

            OnPropertyChanged(nameof(CurrentPositionText));
        }

        private void OnProductAvailabilityToggled(int productId, bool available)
        {
            _productService.UpdateAvailability(productId, available);

            if (DetailPanel != null && DetailPanel.ProductId == productId)
                DetailPanel.Available = available;
        }

        // ── Selection ────────────────────────────────────────────

        private void OnSelectedProductChanged()
        {
            if (_selectedProduct == null)
            {
                DetailPanel = null;
                IsAddingNew = false;
                return;
            }

            IsAddingNew = false;
            LoadDetailForProduct(_selectedProduct.ProductId);
        }

        private void LoadDetailForProduct(int productId)
        {
            var p = _productService.GetAll().First(x => x.Id == productId);
            int usageCount = _productService.GetUsageCount(productId);
            DetailPanel = new ProductDetailViewModel(
                p.Id, p.Description, p.Price, p.Color, p.Available, usageCount, isNew: false);
        }

        /// <summary>Returns to the "no product selected" placeholder.</summary>
        private void BackToPlaceholder()
        {
            _suppressSelectionChange = true;
            SelectedProduct = null;
            _suppressSelectionChange = false;
            DetailPanel = null;
            IsAddingNew = false;
        }

        // ── Add new product ──────────────────────────────────────

        private void BeginAddProduct()
        {
            SelectedProduct = null;
            IsAddingNew = true;
            DetailPanel = new ProductDetailViewModel(
                productId: 0,
                description: string.Empty,
                price: 0m,
                colorAsString: "#5B9BD5",
                available: true,
                usageCount: 0,
                isNew: true);
        }

        // ── Save / Discard ───────────────────────────────────────

        private void SaveDetail()
        {
            if (DetailPanel == null) return;

            decimal price = DetailPanel.ParsedPrice ?? 0m;

            if (IsAddingNew)
            {
                _productService.Add(new Product
                {
                    Description = DetailPanel.Description.Trim(),
                    Price = price,
                    Available = DetailPanel.Available,
                    Color = DetailPanel.ColorAsString
                });
            }
            else
            {
                int sortOrder = Products
                    .Select((p, i) => (p, i))
                    .FirstOrDefault(x => x.p.ProductId == DetailPanel.ProductId).i + 1;

                _productService.Update(new Product
                {
                    Id = DetailPanel.ProductId,
                    Description = DetailPanel.Description.Trim(),
                    Price = price,
                    Available = DetailPanel.Available,
                    Color = DetailPanel.ColorAsString,
                    SortOrder = sortOrder
                });
            }

            BackToPlaceholder();
            LoadData();
        }

        private void DiscardDetail()
        {
            BackToPlaceholder();
        }

        // ── Name lock toggle ─────────────────────────────────────

        private async void ToggleNameLock()
        {
            if (DetailPanel == null) return;

            // Re-locking needs no warning.
            if (DetailPanel.NameUnlocked)
            {
                DetailPanel.NameUnlocked = false;
                return;
            }

            int usage = DetailPanel.UsageCount;
            string msg = usage > 0
                ? $"Dieses Produkt wurde bereits in {usage} Bestellungen verwendet.\n\n" +
                  "Eine Änderung der Bezeichnung beeinflusst die Interpretation dieser historischen Daten. " +
                  "Bitte nur für echte Korrekturen (z. B. Tippfehler) verwenden, nicht für inhaltliche Änderungen.\n\n" +
                  "Trotzdem fortfahren?"
                : "Die Bezeichnung des Produkts entsperren?\n\n" +
                  "Bitte nur für echte Korrekturen verwenden, nicht für eine Neuausrichtung des Produkts.\n\n" +
                  "Fortfahren?";

            if (await _dialogService.ShowConfirmationAsync("Bezeichnung entsperren", msg))
                DetailPanel.NameUnlocked = true;
        }

        // ── Position / Sort order ────────────────────────────────

        private bool CanMoveSelected(int direction)
        {
            if (DetailPanel == null || IsAddingNew) return false;
            int idx = Products.IndexOf(SelectedProduct);
            if (idx < 0) return false;
            int target = idx + direction;
            return target >= 0 && target < Products.Count;
        }

        private void MoveSelected(int direction)
        {
            if (!CanMoveSelected(direction)) return;

            var item = SelectedProduct;
            int idx = Products.IndexOf(item);
            int targetIdx = idx + direction;

            _suppressSelectionChange = true;
            Products.RemoveAt(idx);
            Products.Insert(targetIdx, item);
            SelectedProduct = item;
            _suppressSelectionChange = false;

            _productService.UpdateSortOrder(
                Products.Select((p, i) => new Product { Id = p.ProductId, SortOrder = i + 1 }));

            OnPropertyChanged(nameof(CurrentPositionText));
        }

        // ── Persistence helpers ──────────────────────────────────

        public void SaveSidePanelWidth(double actualWidth)
        {
            _settings.Set("productsSidePanelWidth", actualWidth);
            _settings.Save();
        }
    }
}
