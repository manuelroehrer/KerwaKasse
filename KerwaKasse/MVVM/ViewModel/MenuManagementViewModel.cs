using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;

namespace KerwaKasse.MVVM.ViewModel
{
    /// <summary>
    /// One Speisekarte in the management dialog's sidebar list.
    /// Name and product count are observable so saving updates the list live.
    /// </summary>
    public class MenuListItemViewModel : PropertyChangedBase
    {
        public int Id { get; }

        private string _name;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        private int _productCount;
        public int ProductCount
        {
            get => _productCount;
            set { _productCount = value; OnPropertyChanged(); }
        }

        public MenuListItemViewModel(int id, string name, int productCount)
        {
            Id = id;
            _name = name;
            _productCount = productCount;
        }
    }

    /// <summary>
    /// One product in the menu-product assignment list inside the management dialog.
    /// Toggling only modifies in-memory state — persistence happens on Save.
    /// </summary>
    public class MenuProductItemViewModel : PropertyChangedBase
    {
        private readonly Action _onToggled;

        public int ProductId { get; }
        public string Name { get; }

        /// <summary>The product's colour as a brush for the swatch (like the saved-analysis dialog).</summary>
        public Brush ColorBrush { get; }

        private bool _isIncluded;
        public bool IsIncluded
        {
            get => _isIncluded;
            set
            {
                if (_isIncluded != value)
                {
                    _isIncluded = value;
                    OnPropertyChanged();
                    _onToggled?.Invoke();
                }
            }
        }

        public MenuProductItemViewModel(int productId, string name, string color, bool isIncluded, Action onToggled)
        {
            ProductId = productId;
            Name = name;
            ColorBrush = ColorBorderHelper.ParseBrush(color);
            _isIncluded = isIncluded;
            _onToggled = onToggled;
        }
    }

    /// <summary>
    /// ViewModel for the menu-management ContentDialog.
    /// Allows creating, renaming, deleting Speisekarten, and assigning products.
    /// The name field and the product checkboxes are an edit buffer: changes are only
    /// persisted when the user clicks Save, and reverted by Discard.
    /// </summary>
    public class MenuManagementViewModel : PropertyChangedBase, IDataErrorInfo
    {
        public const int MaxNameLength = 30;

        private readonly IMenuService _menuService;
        private readonly IReadOnlyList<(int Id, string Name, string Color)> _allProducts;
        private HashSet<int> _originalIncludedIds = new();

        // Suppresses dirty tracking while the name field is filled programmatically.
        private bool _suppressNameEdit;

        public ObservableCollection<MenuListItemViewModel> Menus { get; } = new();

        private MenuListItemViewModel _selectedMenu;
        public MenuListItemViewModel SelectedMenu
        {
            get => _selectedMenu;
            set
            {
                if (_selectedMenu == value) return;
                _selectedMenu = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasMenuSelected));
                OnPropertyChanged(nameof(SelectedMenuPositionText));

                _suppressNameEdit = true;
                MenuName = value?.Name ?? string.Empty;
                _suppressNameEdit = false;

                LoadMenuProducts();
            }
        }

        public bool HasMenuSelected => _selectedMenu != null;

        /// <summary>True while at least one Speisekarte exists (drives the sidebar empty state).</summary>
        public bool HasMenus => Menus.Count > 0;

        /// <summary>"X / N" position of the selected card within the list (shown next to the move buttons).</summary>
        public string SelectedMenuPositionText
        {
            get
            {
                if (_selectedMenu == null) return string.Empty;
                int idx = Menus.IndexOf(_selectedMenu);
                return idx >= 0 ? $"{idx + 1} / {Menus.Count}" : string.Empty;
            }
        }

        // ── Name (edit buffer; applied on Save) ──────────────────
        private string _menuName = string.Empty;
        public string MenuName
        {
            get => _menuName;
            set
            {
                if (_menuName == value) return;
                _menuName = value;
                OnPropertyChanged();
                if (!_suppressNameEdit) RecomputeUnsaved();
            }
        }

        // ── Product list for selected menu ───────────────────────
        public ObservableCollection<MenuProductItemViewModel> AllMenuProducts { get; } = new();

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                UpdateFilteredProducts();
            }
        }

        private List<MenuProductItemViewModel> _filteredMenuProducts = new();
        public List<MenuProductItemViewModel> FilteredMenuProducts
        {
            get => _filteredMenuProducts;
            private set
            {
                _filteredMenuProducts = value;
                OnPropertyChanged();
            }
        }

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            private set
            {
                _hasUnsavedChanges = value;
                OnPropertyChanged();
            }
        }

        // ── Commands ─────────────────────────────────────────────
        public RelayCommand AddMenuCommand { get; }
        public RelayCommand DeleteMenuCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand DiscardCommand { get; }
        public RelayCommand MoveMenuUpCommand { get; }
        public RelayCommand MoveMenuDownCommand { get; }
        public RelayCommand SelectAllProductsCommand { get; }
        public RelayCommand ClearProductsCommand { get; }

        public MenuManagementViewModel(IMenuService menuService,
            IEnumerable<(int Id, string Name, string Color)> allProducts)
        {
            _menuService = menuService;
            _allProducts = allProducts.ToList();

            AddMenuCommand = new RelayCommand(_ => AddMenu());
            DeleteMenuCommand = new RelayCommand(_ => DeleteMenu(),
                _ => SelectedMenu != null);
            SaveCommand = new RelayCommand(_ => Save(),
                _ => SelectedMenu != null && HasUnsavedChanges && !HasNameError);
            DiscardCommand = new RelayCommand(_ => Discard(),
                _ => SelectedMenu != null && HasUnsavedChanges);
            MoveMenuUpCommand = new RelayCommand(_ => MoveMenu(-1), _ => CanMoveMenu(-1));
            MoveMenuDownCommand = new RelayCommand(_ => MoveMenu(1), _ => CanMoveMenu(1));
            SelectAllProductsCommand = new RelayCommand(_ => SetAllProducts(true), _ => HasMenuSelected);
            ClearProductsCommand = new RelayCommand(_ => SetAllProducts(false), _ => HasMenuSelected);

            LoadMenus();
        }

        // ── Manual ordering ──────────────────────────────────────

        private bool CanMoveMenu(int direction)
        {
            if (_selectedMenu == null) return false;
            int target = Menus.IndexOf(_selectedMenu) + direction;
            return target >= 0 && target < Menus.Count;
        }

        /// <summary>Moves the selected card one step and persists the new order. Collection.Move keeps
        /// the selection on the same item, so the product view is not reloaded.</summary>
        private void MoveMenu(int direction)
        {
            if (!CanMoveMenu(direction)) return;
            int idx = Menus.IndexOf(_selectedMenu);
            Menus.Move(idx, idx + direction);
            _menuService.UpdateSortOrder(Menus.Select((m, i) => new Menu { Id = m.Id, SortOrder = i + 1 }));
            OnPropertyChanged(nameof(SelectedMenuPositionText));
        }

        private void LoadMenus()
        {
            int? previousId = _selectedMenu?.Id;

            Menus.Clear();
            foreach (var m in _menuService.GetAll())
                Menus.Add(new MenuListItemViewModel(m.Id, m.Name, _menuService.GetProductIds(m.Id).Count));

            OnPropertyChanged(nameof(HasMenus));

            SelectedMenu = previousId.HasValue
                ? Menus.FirstOrDefault(m => m.Id == previousId.Value)
                : null;
        }

        private void LoadMenuProducts()
        {
            AllMenuProducts.Clear();
            HasUnsavedChanges = false;

            if (SelectedMenu == null)
            {
                _originalIncludedIds = new HashSet<int>();
                UpdateFilteredProducts();
                return;
            }

            var includedIds = _menuService.GetProductIds(SelectedMenu.Id).ToHashSet();
            _originalIncludedIds = new HashSet<int>(includedIds);

            foreach (var (id, name, color) in _allProducts)
                AllMenuProducts.Add(new MenuProductItemViewModel(id, name, color, includedIds.Contains(id), OnProductToggled));

            UpdateFilteredProducts();
        }

        private void OnProductToggled() => RecomputeUnsaved();

        private void SetAllProducts(bool included)
        {
            foreach (var p in AllMenuProducts) p.IsIncluded = included;
        }

        /// <summary>Marks the buffer dirty when the name or the product selection differs from what is stored.</summary>
        private void RecomputeUnsaved()
        {
            if (_selectedMenu == null)
            {
                HasUnsavedChanges = false;
                return;
            }

            var name = (_menuName ?? string.Empty).Trim();
            bool nameChanged = name.Length > 0 && name != _selectedMenu.Name;

            var currentIds = AllMenuProducts.Where(p => p.IsIncluded).Select(p => p.ProductId).ToHashSet();
            bool assignmentChanged = !currentIds.SetEquals(_originalIncludedIds);

            HasUnsavedChanges = nameChanged || assignmentChanged;
        }

        private void Save()
        {
            if (_selectedMenu == null) return;

            var name = (_menuName ?? string.Empty).Trim();
            if (name.Length > 0 && name != _selectedMenu.Name)
            {
                _menuService.Rename(_selectedMenu.Id, name);
                _selectedMenu.Name = name;
            }

            var ids = AllMenuProducts.Where(p => p.IsIncluded).Select(p => p.ProductId).ToList();
            _menuService.SetProducts(_selectedMenu.Id, ids);
            _originalIncludedIds = ids.ToHashSet();
            _selectedMenu.ProductCount = ids.Count;

            HasUnsavedChanges = false;
        }

        private void Discard()
        {
            if (_selectedMenu == null) return;

            _suppressNameEdit = true;
            MenuName = _selectedMenu.Name;
            _suppressNameEdit = false;

            LoadMenuProducts();
        }

        private void AddMenu()
        {
            int newId = _menuService.Add(GenerateUniqueMenuName());
            LoadMenus();
            SelectedMenu = Menus.FirstOrDefault(m => m.Id == newId);
        }

        /// <summary>"Neue Speisekarte", then "Neue Speisekarte 2", "… 3", … to avoid duplicate default names.</summary>
        private string GenerateUniqueMenuName()
        {
            const string baseName = "Neue Speisekarte";
            var existing = _menuService.GetAll()
                .Select(m => m.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!existing.Contains(baseName)) return baseName;
            int n = 2;
            while (existing.Contains($"{baseName} {n}")) n++;
            return $"{baseName} {n}";
        }

        private void DeleteMenu()
        {
            if (SelectedMenu == null) return;
            _menuService.Delete(SelectedMenu.Id);
            SelectedMenu = null;
            LoadMenus();
        }

        private void UpdateFilteredProducts()
        {
            var search = _searchText?.Trim() ?? string.Empty;
            FilteredMenuProducts = string.IsNullOrEmpty(search)
                ? AllMenuProducts.ToList()
                : AllMenuProducts
                    .Where(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
                    .ToList();
        }

        // ── Name validation ──────────────────────────────────────
        public bool HasNameError => ValidateName().Length > 0;

        /// <summary>Empty, too long, or a name already used by another Speisekarte are rejected.</summary>
        private string ValidateName()
        {
            if (_selectedMenu == null) return string.Empty;

            var name = (_menuName ?? string.Empty).Trim();
            if (name.Length == 0) return "Name darf nicht leer sein";
            if (name.Length > MaxNameLength) return $"Maximal {MaxNameLength} Zeichen erlaubt";
            if (Menus.Any(m => m.Id != _selectedMenu.Id
                               && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
                return "Name ist bereits vergeben";

            return string.Empty;
        }

        string IDataErrorInfo.Error => string.Empty;

        public string this[string columnName] =>
            columnName == nameof(MenuName) ? ValidateName() : string.Empty;
    }
}
