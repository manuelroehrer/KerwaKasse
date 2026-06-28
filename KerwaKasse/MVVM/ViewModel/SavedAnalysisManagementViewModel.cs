using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;

namespace KerwaKasse.MVVM.ViewModel
{
    /// <summary>One saved analysis in the management dialog's sidebar list.</summary>
    public class SavedAnalysisItemViewModel : PropertyChangedBase
    {
        public int Id { get; }

        private string _name;
        public string Name { get => _name; set { _name = value; OnPropertyChanged(); } }

        private string _summary;
        public string Summary { get => _summary; set { _summary = value; OnPropertyChanged(); } }

        public SavedAnalysisItemViewModel(int id, string name, string summary)
        {
            Id = id;
            _name = name;
            _summary = summary;
        }
    }

    /// <summary>
    /// Full editor for saved analyses (create, edit name/products/period, duplicate, delete, reorder).
    /// The detail pane mirrors the analysis view's manual controls so an analysis can be defined
    /// completely here, not just renamed.
    /// </summary>
    public class SavedAnalysisManagementViewModel : PropertyChangedBase, IDataErrorInfo
    {
        public const int MaxNameLength = 40;

        private readonly ISavedAnalysisService _service;
        private readonly List<Product> _products;
        private List<SavedAnalysis> _all = new();
        private bool _suppress;

        // Snapshot of the loaded analysis, so editing back to it clears the "changed" state.
        private string _originalName = string.Empty;
        private HashSet<int> _originalProductIds = new();
        private DateTime _originalFrom;
        private DateTime _originalTo;

        public ObservableCollection<SavedAnalysisItemViewModel> Items { get; } = new();
        public ObservableCollection<DateTime> TimeFrom_Values { get; }
        public ObservableCollection<DateTime> TimeTo_Values { get; }
        public ObservableCollection<ProductFilterItem> EditProducts { get; } = new();

        public SavedAnalysisManagementViewModel(ISavedAnalysisService service, IProductService productService)
        {
            _service = service;
            _products = productService.GetAll();

            TimeFrom_Values = new ObservableCollection<DateTime>();
            TimeTo_Values = new ObservableCollection<DateTime>();
            for (int i = 0; i < 24; i++)
            {
                TimeFrom_Values.Add(TimeOfDay(i, 0));
                TimeTo_Values.Add(TimeOfDay(i, 0));
            }
            TimeTo_Values.Add(TimeOfDay(23, 59));

            _dateFrom = DateTime.Today;
            _dateTo = DateTime.Today;
            _timeFrom = TimeFrom_Values[0];
            _timeTo = TimeTo_Values[^1];

            AddCommand = new RelayCommand(_ => AddNew());
            DeleteCommand = new RelayCommand(_ => Delete(), _ => HasSelection);
            DuplicateCommand = new RelayCommand(_ => Duplicate(), _ => HasSelection);
            SaveCommand = new RelayCommand(_ => Save(), _ => HasSelection && IsDirty && !HasNameError);
            DiscardCommand = new RelayCommand(_ => Discard(), _ => HasSelection && IsDirty);
            MoveUpCommand = new RelayCommand(_ => Move(-1), _ => CanMove(-1));
            MoveDownCommand = new RelayCommand(_ => Move(1), _ => CanMove(1));
            SelectAllProductsCommand = new RelayCommand(_ => SetAllProducts(true));
            ClearProductsCommand = new RelayCommand(_ => SetAllProducts(false));

            Load();
        }

        private SavedAnalysisItemViewModel _selected;
        public SavedAnalysisItemViewModel Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedPositionText));
                LoadBuffer();
            }
        }

        public bool HasSelection => _selected != null;
        public bool HasItems => Items.Count > 0;

        public string SelectedPositionText
        {
            get
            {
                if (_selected == null) return string.Empty;
                int idx = Items.IndexOf(_selected);
                return idx >= 0 ? $"{idx + 1} / {Items.Count}" : string.Empty;
            }
        }

        // ── Edit buffer ──
        private string _editName = string.Empty;
        public string EditName
        {
            get => _editName;
            set { if (_editName == value) return; _editName = value; OnPropertyChanged(); if (!_suppress) RecomputeDirty(); }
        }

        private DateTime _dateFrom;
        public DateTime DateFrom { get => _dateFrom; set { _dateFrom = value; OnPropertyChanged(); if (!_suppress) RecomputeDirty(); } }

        private DateTime _timeFrom;
        public DateTime TimeFrom { get => _timeFrom; set { _timeFrom = value; OnPropertyChanged(); if (!_suppress) RecomputeDirty(); } }

        private DateTime _dateTo;
        public DateTime DateTo { get => _dateTo; set { _dateTo = value; OnPropertyChanged(); if (!_suppress) RecomputeDirty(); } }

        private DateTime _timeTo;
        public DateTime TimeTo { get => _timeTo; set { _timeTo = value; OnPropertyChanged(); if (!_suppress) RecomputeDirty(); } }

        private string _productSearchText = string.Empty;
        public string ProductSearchText
        {
            get => _productSearchText;
            set { _productSearchText = value; OnPropertyChanged(); UpdateFilteredProducts(); }
        }

        private List<ProductFilterItem> _filteredProducts = new();
        public List<ProductFilterItem> FilteredProducts
        {
            get => _filteredProducts;
            private set { _filteredProducts = value; OnPropertyChanged(); }
        }

        public string ProductSelectionSummary
        {
            get
            {
                int sel = EditProducts.Count(p => p.IsSelected);
                if (sel == 0) return "keine Produkte";
                return sel == EditProducts.Count ? $"alle ({EditProducts.Count})" : $"{sel} von {EditProducts.Count}";
            }
        }

        private bool _isDirty;
        public bool IsDirty { get => _isDirty; private set { _isDirty = value; OnPropertyChanged(); } }

        // ── Commands ──
        public RelayCommand AddCommand { get; }
        public RelayCommand DeleteCommand { get; }
        public RelayCommand DuplicateCommand { get; }
        public RelayCommand SaveCommand { get; }
        public RelayCommand DiscardCommand { get; }
        public RelayCommand MoveUpCommand { get; }
        public RelayCommand MoveDownCommand { get; }
        public RelayCommand SelectAllProductsCommand { get; }
        public RelayCommand ClearProductsCommand { get; }

        private void Load()
        {
            int? previousId = _selected?.Id;
            _all = _service.GetAll();

            Items.Clear();
            foreach (var a in _all)
                Items.Add(new SavedAnalysisItemViewModel(a.Id, a.Name, Summarize(a)));

            OnPropertyChanged(nameof(HasItems));
            Selected = previousId.HasValue ? Items.FirstOrDefault(i => i.Id == previousId.Value) : null;
        }

        private void LoadBuffer()
        {
            _suppress = true;

            _productSearchText = string.Empty;
            OnPropertyChanged(nameof(ProductSearchText));

            var a = _selected == null ? null : _all.FirstOrDefault(x => x.Id == _selected.Id);

            EditName = a?.Name ?? string.Empty;

            if (a != null && a.From.HasValue && a.To.HasValue)
            {
                _dateFrom = a.From.Value.Date;
                _timeFrom = NearestTimeValue(TimeFrom_Values, a.From.Value);
                _dateTo = a.To.Value.Date;
                _timeTo = NearestTimeValue(TimeTo_Values, a.To.Value);
            }
            else
            {
                _dateFrom = DateTime.Today;
                _dateTo = DateTime.Today;
                _timeFrom = TimeFrom_Values[0];
                _timeTo = TimeTo_Values[^1];
            }
            OnPropertyChanged(nameof(DateFrom));
            OnPropertyChanged(nameof(TimeFrom));
            OnPropertyChanged(nameof(DateTo));
            OnPropertyChanged(nameof(TimeTo));

            EditProducts.Clear();
            HashSet<int> included = null;
            if (a != null && !a.AllProducts)
                included = _service.GetProductIds(a.Id).ToHashSet();

            foreach (var p in _products)
            {
                bool isOn = a != null && (a.AllProducts || (included?.Contains(p.Id) ?? false));
                EditProducts.Add(new ProductFilterItem(p.Id, p.Name, p.Color, isOn, OnProductToggled));
            }
            UpdateFilteredProducts();
            OnPropertyChanged(nameof(ProductSelectionSummary));

            CaptureOriginals();
            _suppress = false;
            IsDirty = false;
        }

        private void OnProductToggled()
        {
            OnPropertyChanged(nameof(ProductSelectionSummary));
            if (!_suppress) RecomputeDirty();
        }

        private void SetAllProducts(bool selected)
        {
            _suppress = true;
            foreach (var p in EditProducts) p.IsSelected = selected;
            _suppress = false;
            OnPropertyChanged(nameof(ProductSelectionSummary));
            RecomputeDirty();
        }

        private void UpdateFilteredProducts()
        {
            var search = _productSearchText?.Trim() ?? string.Empty;
            FilteredProducts = string.IsNullOrEmpty(search)
                ? EditProducts.ToList()
                : EditProducts.Where(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private DateTime CurrentFrom() =>
            new(_dateFrom.Year, _dateFrom.Month, _dateFrom.Day, _timeFrom.Hour, _timeFrom.Minute, 0);

        private DateTime CurrentTo() =>
            new(_dateTo.Year, _dateTo.Month, _dateTo.Day, _timeTo.Hour, _timeTo.Minute, _timeTo.Second);

        /// <summary>Snapshots the current buffer as the "saved" baseline (after load and after save).</summary>
        private void CaptureOriginals()
        {
            _originalName = (_editName ?? string.Empty).Trim();
            _originalProductIds = EditProducts.Where(p => p.IsSelected).Select(p => p.ProductId).ToHashSet();
            _originalFrom = CurrentFrom();
            _originalTo = CurrentTo();
        }

        /// <summary>Dirty only while the buffer actually differs from the loaded analysis — editing back
        /// to the original state (e.g. unticking and re-ticking a product) clears it again.</summary>
        private void RecomputeDirty()
        {
            if (_selected == null) { IsDirty = false; return; }

            var name = (_editName ?? string.Empty).Trim();
            bool nameChanged = name.Length > 0 && name != _originalName;
            var currentIds = EditProducts.Where(p => p.IsSelected).Select(p => p.ProductId).ToHashSet();
            bool productsChanged = !currentIds.SetEquals(_originalProductIds);
            bool rangeChanged = CurrentFrom() != _originalFrom || CurrentTo() != _originalTo;

            IsDirty = nameChanged || productsChanged || rangeChanged;
        }

        private SavedAnalysis BuildFromBuffer(int id)
        {
            var included = EditProducts.Where(p => p.IsSelected).Select(p => p.ProductId).ToList();
            bool all = EditProducts.Count > 0 && included.Count == EditProducts.Count;
            return new SavedAnalysis
            {
                Id = id,
                Name = (_editName ?? string.Empty).Trim(),
                AllProducts = all,
                ProductIds = all ? new List<int>() : included,
                ProductCount = included.Count, // so the list summary updates correctly right after Save
                From = CurrentFrom(),
                To = CurrentTo()
            };
        }

        private void Save()
        {
            if (_selected == null || HasNameError) return;
            var a = BuildFromBuffer(_selected.Id);
            _service.Update(a);

            // Refresh cache + the list row without dropping the selection.
            var cached = _all.FirstOrDefault(x => x.Id == a.Id);
            if (cached != null)
            {
                cached.Name = a.Name;
                cached.AllProducts = a.AllProducts;
                cached.ProductCount = a.ProductIds.Count;
                cached.From = a.From;
                cached.To = a.To;
            }
            _selected.Name = a.Name;
            _selected.Summary = Summarize(a);
            CaptureOriginals();
            IsDirty = false;
        }

        private void Discard()
        {
            if (_selected == null) return;
            LoadBuffer();
        }

        private void Delete()
        {
            if (_selected == null) return;
            _service.Delete(_selected.Id);
            Selected = null;
            Load();
        }

        private void AddNew()
        {
            var def = new SavedAnalysis
            {
                Name = UniqueName("Neue Auswertung"),
                AllProducts = true,
                From = DateTime.Today,
                To = DateTime.Today.AddHours(23).AddMinutes(59)
            };
            int id = _service.Add(def);
            Load();
            Selected = Items.FirstOrDefault(i => i.Id == id);
        }

        private void Duplicate()
        {
            if (_selected == null) return;
            var src = _all.FirstOrDefault(a => a.Id == _selected.Id);
            if (src == null) return;

            var copy = new SavedAnalysis
            {
                Name = UniqueName(src.Name + " (Kopie)"),
                AllProducts = src.AllProducts,
                ProductIds = src.AllProducts ? new List<int>() : _service.GetProductIds(src.Id),
                From = src.From,
                To = src.To
            };
            int id = _service.Add(copy);
            Load();
            Selected = Items.FirstOrDefault(i => i.Id == id);
        }

        private string UniqueName(string baseName)
        {
            var existing = _all.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains(baseName)) return baseName;
            int n = 2;
            while (existing.Contains($"{baseName} {n}")) n++;
            return $"{baseName} {n}";
        }

        private bool CanMove(int direction)
        {
            if (_selected == null) return false;
            int target = Items.IndexOf(_selected) + direction;
            return target >= 0 && target < Items.Count;
        }

        private void Move(int direction)
        {
            if (!CanMove(direction)) return;
            int idx = Items.IndexOf(_selected);
            Items.Move(idx, idx + direction);
            _service.UpdateSortOrder(Items.Select((it, i) => new SavedAnalysis { Id = it.Id, SortOrder = i + 1 }));
            OnPropertyChanged(nameof(SelectedPositionText));
        }

        public static string Summarize(SavedAnalysis a)
        {
            string products = a.AllProducts ? "alle Produkte" : $"{a.ProductCount} Produkte";
            string period;
            if (a.From.HasValue && a.To.HasValue)
                period = a.From.Value.Date == a.To.Value.Date
                    ? a.From.Value.ToString("dd.MM.yyyy")
                    : $"{a.From.Value:dd.MM.}–{a.To.Value:dd.MM.yyyy}";
            else
                period = "ohne Zeitraum";
            return $"{products} · {period}";
        }

        private static DateTime TimeOfDay(int hour, int minute) =>
            new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, hour, minute, 0);

        private static DateTime NearestTimeValue(ObservableCollection<DateTime> values, DateTime time)
        {
            var target = time.TimeOfDay;
            return values.OrderBy(v => Math.Abs((v.TimeOfDay - target).Ticks)).First();
        }

        public bool HasNameError => Validate().Length > 0;

        private string Validate()
        {
            if (_selected == null) return string.Empty;
            var name = (_editName ?? string.Empty).Trim();
            if (name.Length == 0) return "Name darf nicht leer sein";
            if (name.Length > MaxNameLength) return $"Maximal {MaxNameLength} Zeichen erlaubt";
            if (Items.Any(i => i.Id != _selected.Id && string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase)))
                return "Name ist bereits vergeben";
            return string.Empty;
        }

        string IDataErrorInfo.Error => string.Empty;

        public string this[string columnName] =>
            columnName == nameof(EditName) ? Validate() : string.Empty;
    }
}
