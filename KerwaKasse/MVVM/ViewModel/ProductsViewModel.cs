using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
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
        private readonly IMenuService _menuService;
        private readonly ISettingsService _settings;
        private readonly IDialogService _dialogService;

        // ── Product list ─────────────────────────────────────────
        public ObservableCollection<ProductListItemViewModel> Products { get; } = new();

        // ── Product search (filters the list above) ──────────────
        private ICollectionView _productsView;
        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                _productsView?.Refresh();
            }
        }

        private bool ProductFilter(object item) =>
            item is ProductListItemViewModel p &&
            (string.IsNullOrWhiteSpace(_searchText) ||
             p.Name.Contains(_searchText.Trim(), StringComparison.OrdinalIgnoreCase));

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

        // ── Menus / Speisekarten ─────────────────────────────────
        private List<Menu> _menus = new();
        public List<Menu> Menus
        {
            get => _menus;
            private set
            {
                _menus = value;
                OnPropertyChanged();
            }
        }

        private Menu _selectedMenu;
        public Menu SelectedMenu
        {
            get => _selectedMenu;
            set
            {
                _selectedMenu = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasActiveMenu));
                OnPropertyChanged(nameof(ActiveMenuText));
                RefreshActiveMenuFlags();
            }
        }

        /// <summary>Menu entries shown in the ProductsView dropdown (name + active marker).</summary>
        public ObservableCollection<MenuOptionViewModel> MenuOptions { get; } = new();

        /// <summary>True while a Speisekarte exactly matches the current availabilities.</summary>
        public bool HasActiveMenu => _selectedMenu != null;

        /// <summary>Name of the active Speisekarte, or null when none matches.</summary>
        public string ActiveMenuText => _selectedMenu?.Name;

        private void RefreshActiveMenuFlags()
        {
            foreach (var o in MenuOptions)
                o.IsActive = _selectedMenu != null && o.Menu.Id == _selectedMenu.Id;
        }

        // ── Commands ─────────────────────────────────────────────
        public RelayCommand AddProductCommand { get; }
        public RelayCommand SaveDetailCommand { get; }
        public RelayCommand DiscardDetailCommand { get; }
        public RelayCommand ToggleNameLockCommand { get; }
        public RelayCommand MoveSelectedUpCommand { get; }
        public RelayCommand MoveSelectedDownCommand { get; }
        public RelayCommand BeginPositionEditCommand { get; }
        public RelayCommand ApplyMenuCommand { get; }
        public RelayCommand OpenMenuManagementCommand { get; }

        public ProductsViewModel(
            IProductService productService,
            IMenuService menuService,
            ISettingsService settings,
            IDialogService dialogService)
        {
            _productService = productService;
            _menuService = menuService;
            _settings = settings;
            _dialogService = dialogService;

            _productsView = CollectionViewSource.GetDefaultView(Products);
            _productsView.Filter = ProductFilter;

            LoadPreferences();

            AddProductCommand = new RelayCommand(_ => BeginAddProduct());
            SaveDetailCommand = new RelayCommand(_ => SaveDetail(),
                _ => DetailPanel != null && !DetailPanel.HasErrors && (DetailPanel.IsNew || DetailPanel.IsDirty));
            DiscardDetailCommand = new RelayCommand(_ => DiscardDetail(), _ => DetailPanel != null);
            ToggleNameLockCommand = new RelayCommand(_ => ToggleNameLock(), _ => DetailPanel is { IsNew: false });
            MoveSelectedUpCommand = new RelayCommand(_ => MoveSelected(-1), _ => CanMoveSelected(-1));
            MoveSelectedDownCommand = new RelayCommand(_ => MoveSelected(1), _ => CanMoveSelected(1));
            BeginPositionEditCommand = new RelayCommand(_ => BeginPositionEdit(), _ => SelectedProduct != null && !IsAddingNew);
            ApplyMenuCommand = new RelayCommand(o => ApplyMenu(o as Menu), o => (o as Menu) != null || SelectedMenu != null);
            OpenMenuManagementCommand = new RelayCommand(_ => OpenMenuManagement());

            LoadMenus();
            LoadData();
        }

        // ── Preferences ──────────────────────────────────────────

        private void LoadPreferences()
        {
            SidePanelWidth = _settings.Get("productsSidePanelWidth", 440.0);
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
                    p.Id, p.Name, p.Price, p.Color, p.Available,
                    OnProductAvailabilityToggled));
            }

            if (selected != null)
                SelectedProduct = Products.FirstOrDefault(p => p.ProductId == selected.ProductId);

            DetectActiveMenu();
            OnPropertyChanged(nameof(CurrentPositionText));
        }

        private void OnProductAvailabilityToggled(int productId, bool available)
        {
            _productService.UpdateAvailability(productId, available);

            if (DetailPanel != null && DetailPanel.ProductId == productId)
                DetailPanel.AcceptPersistedAvailability(available);

            DetectActiveMenu();
        }

        public void LoadMenus()
        {
            var menus = _menuService.GetAll();
            int? previousId = SelectedMenu?.Id;
            Menus = menus;

            MenuOptions.Clear();
            foreach (var m in menus)
                MenuOptions.Add(new MenuOptionViewModel(m, previousId == m.Id));

            SelectedMenu = previousId.HasValue
                ? menus.FirstOrDefault(m => m.Id == previousId.Value)
                : null;
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
                p.Id, p.Name, p.Price, p.Color, p.Available, usageCount, isNew: false);
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
                    Name = DetailPanel.Name.Trim(),
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
                    Name = DetailPanel.Name.Trim(),
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
                  "Eine Änderung des Namens beeinflusst die Interpretation dieser historischen Daten. " +
                  "Bitte nur für echte Korrekturen (z. B. Tippfehler) verwenden, nicht für inhaltliche Änderungen.\n\n" +
                  "Trotzdem fortfahren?"
                : "Den Namen des Produkts entsperren?\n\n" +
                  "Bitte nur für echte Korrekturen verwenden, nicht für eine Neuausrichtung des Produkts.\n\n" +
                  "Fortfahren?";

            if (await _dialogService.ShowConfirmationAsync("Name entsperren", msg))
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
            MoveSelectedToIndex(Products.IndexOf(SelectedProduct) + direction);
        }

        /// <summary>Moves the selected product to the given index and persists the whole order once.</summary>
        private void MoveSelectedToIndex(int targetIdx)
        {
            var item = SelectedProduct;
            int idx = Products.IndexOf(item);
            if (idx < 0 || targetIdx == idx) return;

            _suppressSelectionChange = true;
            Products.RemoveAt(idx);
            Products.Insert(targetIdx, item);
            SelectedProduct = item;
            _suppressSelectionChange = false;

            _productService.UpdateSortOrder(
                Products.Select((p, i) => new Product { Id = p.ProductId, SortOrder = i + 1 }));

            OnPropertyChanged(nameof(CurrentPositionText));
        }

        // ── Direct position input (clicking "X / N" swaps it for a small text box) ──

        private bool _isEditingPosition;
        public bool IsEditingPosition
        {
            get => _isEditingPosition;
            private set { _isEditingPosition = value; OnPropertyChanged(); }
        }

        private string _positionInput = string.Empty;
        public string PositionInput
        {
            get => _positionInput;
            set { _positionInput = value; OnPropertyChanged(); }
        }

        private void BeginPositionEdit()
        {
            if (SelectedProduct == null || IsAddingNew) return;
            int idx = Products.IndexOf(SelectedProduct);
            if (idx < 0) return;
            PositionInput = (idx + 1).ToString();
            IsEditingPosition = true;
        }

        public void CancelPositionEdit() => IsEditingPosition = false;

        /// <summary>Applies the typed position, clamped to 1..N; non-numeric input is discarded.</summary>
        public void CommitPositionEdit()
        {
            if (!IsEditingPosition) return;
            IsEditingPosition = false;

            if (SelectedProduct == null || !int.TryParse(PositionInput?.Trim(), out int position)) return;
            MoveSelectedToIndex(Math.Clamp(position - 1, 0, Products.Count - 1));
        }

        // ── Persistence helpers ──────────────────────────────────

        public void SaveSidePanelWidth(double actualWidth)
        {
            _settings.Set("productsSidePanelWidth", actualWidth);
            _settings.Save();
        }

        // ── Menu management (ContentDialog) ──────────────────────

        private async void OpenMenuManagement()
        {
            var products = Products.Select(p => (p.ProductId, p.Name, p.ColorAsString));
            var menuVm = new MenuManagementViewModel(_menuService, products);
            await _dialogService.ShowMenuManagementAsync(menuVm);

            // Menu definitions may have changed → refresh menu list + active-menu detection.
            LoadMenus();
            LoadData();
        }

        // ── Apply Speisekarte ────────────────────────────────────

        private async void ApplyMenu(Menu menu)
        {
            menu ??= SelectedMenu;
            if (menu == null) return;

            string msg = $"Nur Produkte, die in der Speisekarte \"{menu.Name}\" enthalten sind, " +
                         "werden auf verfügbar gesetzt, alle anderen auf nicht verfügbar.";

            if (!await _dialogService.ShowConfirmationAsync("Speisekarte anwenden?", msg)) return;

            _menuService.ApplyMenu(menu.Id);
            LoadData();
        }

        // ── Active menu detection ────────────────────────────────

        /// <summary>Selects the menu whose product set exactly matches the currently available
        /// products, or clears the selection when no menu matches.</summary>
        private void DetectActiveMenu()
        {
            var availableIds = Products
                .Where(p => p.Available)
                .Select(p => p.ProductId)
                .ToHashSet();

            foreach (var menu in Menus)
            {
                var menuProductIds = _menuService.GetProductIds(menu.Id).ToHashSet();
                if (availableIds.SetEquals(menuProductIds))
                {
                    SelectedMenu = menu;
                    return;
                }
            }

            SelectedMenu = null;
        }
    }
}
