using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.Logging;
using SkiaSharp;

namespace KerwaKasse.MVVM.ViewModel
{
    /// <summary>Time quick-pick presets. They simply fill the always-visible from/to fields.</summary>
    public enum QuickRange
    {
        Today,
        Yesterday,
        Last7Days
    }

    /// <summary>
    /// The "Analyse" view model. Opens on today's sales (loaded automatically), with a free product
    /// filter and either a product overview or a time course. A saved analysis stores only name +
    /// products + period (NOT the chosen view). Whether one is "active" is detected at runtime by
    /// comparing the current filter to the stored ones. Read-only: it never mutates orders.
    /// </summary>
    public class AnalysisViewModel : PropertyChangedBase
    {
        private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
        private static readonly SKColor AccentColor = SKColor.Parse("#1B4068");

        private readonly IAnalyticsService _analytics;
        private readonly IProductService _productService;
        private readonly ISavedAnalysisService _savedAnalysisService;
        private readonly IDialogService _dialogService;
        private readonly ILogger<AnalysisViewModel> _logger;

        private bool _suppressReload;
        private AnalysisTimeResolution _resolution = AnalysisTimeResolution.Hour;

        // The time course is only (re)built while its tab is visible; a reload behind the breakdown
        // tab just marks it stale and the next switch to the tab catches up. Building it eagerly
        // cost seconds on wide ranges (the gap-filled buckets explode: a whole-history range means
        // >100k quarter-hour columns) even though the time course is the rarely used view of the two.
        private bool _timeCourseIsStale = true;

        private DateTime _currentFrom;
        private DateTime _currentTo;
        private IReadOnlyCollection<int> _currentIds;
        private bool _currentIncludeFreeEntries = true;
        private List<SavedAnalysis> _savedCache = new();
        private readonly Dictionary<int, HashSet<int>> _savedProductSets = new();

        // Loaded figures plus their per-product price lines and free entries, kept so re-sorting the
        // table only rebuilds rows/pie without hitting the database again.
        private List<SalesFigure> _currentFigures = new();
        private Dictionary<int, IReadOnlyList<PriceDetailLine>> _priceDetailsByProduct = new();
        private ILookup<int?, FreeEntryGroup> _freeEntryGroups = Array.Empty<FreeEntryGroup>().ToLookup(g => g.ProductId);

        // Rows the user expanded (by AnalysisSalesRow.Key); they stay open across re-sorts and reloads.
        private readonly HashSet<string> _expandedRows = new();
        private string _sortColumn = "Amount";
        private bool _sortDescending = true;

        // Metric the pie slices represent. Follows the sorted column; sorting by name keeps the
        // last numeric metric (a name is not quantifiable).
        private string _pieMetricColumn = "Amount";

        public AnalysisViewModel(
            IAnalyticsService analytics,
            IProductService productService,
            ISavedAnalysisService savedAnalysisService,
            IDialogService dialogService,
            ILogger<AnalysisViewModel> logger)
        {
            _analytics = analytics;
            _productService = productService;
            _savedAnalysisService = savedAnalysisService;
            _dialogService = dialogService;
            _logger = logger;

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

            QuickRanges = new ObservableCollection<AnalysisSegmentItem>
            {
                new("Heute", QuickRange.Today, isSelected: true),
                new("Gestern", QuickRange.Yesterday),
                new("7 Tage", QuickRange.Last7Days)
            };

            // "Stunde" stays the default even though it is no longer the first segment.
            Resolutions = new ObservableCollection<AnalysisSegmentItem>
            {
                new("Tag", AnalysisTimeResolution.Day),
                new("Stunde", AnalysisTimeResolution.Hour, isSelected: true),
                new("15 Min", AnalysisTimeResolution.QuarterHour)
            };

            ViewModes = new ObservableCollection<AnalysisSegmentItem>
            {
                new("Übersicht", "Breakdown", isSelected: true),
                new("Zeitverlauf", "TimeCourse")
            };

            _timeCourseColumn = new ColumnSeries<double>
            {
                Name = "Menge",
                Values = Array.Empty<double>(),
                Fill = new SolidColorPaint(AccentColor)
            };
            TimeCourseSeries = new ISeries[] { _timeCourseColumn };
            TimeCourseXAxes = new[] { _timeCourseXAxis };
            TimeCourseYAxes = new[] { _timeCourseYAxis };

            // "Freie Eingabe" first (like in the history's "Position nachtragen"), then the products.
            ProductFilter = new ObservableCollection<ProductFilterItem> { ProductFilterItem.FreeEntries(isSelected: true, OnProductToggled) };
            foreach (var p in _productService.GetAll())
                ProductFilter.Add(new ProductFilterItem(p.Id, p.Name, p.Color, isSelected: true, OnProductToggled));
            UpdateFilteredProducts();

            SavedAnalyses = new ObservableCollection<SavedAnalysisListItem>();

            SelectQuickRangeCommand = new RelayCommand(o => SelectQuickRange(o as AnalysisSegmentItem));
            SelectResolutionCommand = new RelayCommand(o => SelectResolution(o as AnalysisSegmentItem));
            SetViewModeCommand = new RelayCommand(o => SelectViewMode(o as AnalysisSegmentItem));
            SelectAllProductsCommand = new RelayCommand(o => SetAllProducts(true));
            ClearProductsCommand = new RelayCommand(o => SetAllProducts(false));
            ExportCsvCommand = new RelayCommand(o => ExportCsv());
            ExportPdfCommand = new RelayCommand(async o => await ExportPdfAsync());
            SortBreakdownCommand = new RelayCommand(o => SortBreakdown(o as string));
            ToggleRowCommand = new RelayCommand(o => ToggleRow(o as AnalysisSalesRow));
            SaveCurrentCommand = new RelayCommand(async o => await SaveCurrentAsync(), o => !HasActiveAnalysis);
            ApplySavedCommand = new RelayCommand(o => ApplySaved(ToId(o)));
            ManageCommand = new RelayCommand(async o => await ManageAsync());

            LoadSavedAnalyses();
            SelectQuickRange(QuickRanges[0]); // fills the from/to fields and loads today's data
        }

        // ── Time range (always visible; quick-picks just fill the fields) ──
        public ObservableCollection<AnalysisSegmentItem> QuickRanges { get; }
        public ObservableCollection<DateTime> TimeFrom_Values { get; }
        public ObservableCollection<DateTime> TimeTo_Values { get; }

        private DateTime _dateFrom;
        public DateTime DateFrom { get => _dateFrom; set { _dateFrom = value; OnPropertyChanged(); OnCustomEdited(); } }

        private DateTime _timeFrom;
        public DateTime TimeFrom { get => _timeFrom; set { _timeFrom = value; OnPropertyChanged(); OnCustomEdited(); } }

        private DateTime _dateTo;
        public DateTime DateTo { get => _dateTo; set { _dateTo = value; OnPropertyChanged(); OnCustomEdited(); } }

        private DateTime _timeTo;
        public DateTime TimeTo { get => _timeTo; set { _timeTo = value; OnPropertyChanged(); OnCustomEdited(); } }

        // ── View mode + resolution ──
        private bool _isBreakdown = true;
        public bool IsBreakdown { get => _isBreakdown; private set { _isBreakdown = value; OnPropertyChanged(); } }

        private bool _isTimeCourse;
        public bool IsTimeCourse { get => _isTimeCourse; private set { _isTimeCourse = value; OnPropertyChanged(); } }

        public ObservableCollection<AnalysisSegmentItem> Resolutions { get; }
        public ObservableCollection<AnalysisSegmentItem> ViewModes { get; }

        // ── Product filter ──
        public ObservableCollection<ProductFilterItem> ProductFilter { get; }

        private List<ProductFilterItem> _filteredProductFilter = new();
        public List<ProductFilterItem> FilteredProductFilter
        {
            get => _filteredProductFilter;
            private set { _filteredProductFilter = value; OnPropertyChanged(); }
        }

        private string _productSearchText = string.Empty;
        public string ProductSearchText
        {
            get => _productSearchText;
            set { _productSearchText = value; OnPropertyChanged(); UpdateFilteredProducts(); }
        }

        private string _productFilterSummary = string.Empty;
        public string ProductFilterSummary
        {
            get => _productFilterSummary;
            private set { _productFilterSummary = value; OnPropertyChanged(); }
        }

        // ── Results ──
        private ObservableCollection<AnalysisSalesRow> _breakdown = new();
        public ObservableCollection<AnalysisSalesRow> Breakdown
        {
            get => _breakdown;
            private set { _breakdown = value; OnPropertyChanged(); }
        }

        private int _totalAmount;
        public int TotalAmount
        {
            get => _totalAmount;
            private set { _totalAmount = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasData)); }
        }

        private decimal _totalRevenue;
        public decimal TotalRevenue { get => _totalRevenue; private set { _totalRevenue = value; OnPropertyChanged(); } }

        public bool HasData => TotalAmount > 0;

        // ── Table sorting (header click cycles the column, second click flips the direction) ──
        public string NameSortIndicator => SortIndicator("Name");
        public string AmountSortIndicator => SortIndicator("Amount");
        public string RevenueSortIndicator => SortIndicator("Revenue");

        // ▼ always marks a column's own default (first-click) direction, ▲ the other — so a first
        // click on any header shows ▼, regardless of which column. Name's default is ascending
        // (A→Z reads naturally), Amount/Revenue's is descending (largest first).
        private string SortIndicator(string column)
        {
            if (_sortColumn != column) return string.Empty;
            bool isDefaultDirection = column == "Name" ? !_sortDescending : _sortDescending;
            return isDefaultDirection ? " ▼" : " ▲";
        }

        private void SortBreakdown(string column)
        {
            if (string.IsNullOrEmpty(column)) return;

            if (_sortColumn == column)
            {
                _sortDescending = !_sortDescending;
            }
            else
            {
                _sortColumn = column;
                _sortDescending = column != "Name"; // names read naturally A→Z, numbers largest-first
            }

            if (column != "Name")
                _pieMetricColumn = column;

            OnPropertyChanged(nameof(NameSortIndicator));
            OnPropertyChanged(nameof(AmountSortIndicator));
            OnPropertyChanged(nameof(RevenueSortIndicator));
            ApplyBreakdown();
        }

        private List<SalesFigure> SortFigures(List<SalesFigure> figures) => (_sortColumn switch
        {
            // By name a product's special prices follow it directly, and the free entries come last.
            "Name" => (_sortDescending
                    ? figures.OrderBy(f => f.Kind == SalesFigureKind.FreeEntries)
                        .ThenByDescending(f => f.ProductName, StringComparer.CurrentCultureIgnoreCase)
                    : figures.OrderBy(f => f.Kind == SalesFigureKind.FreeEntries)
                        .ThenBy(f => f.ProductName, StringComparer.CurrentCultureIgnoreCase))
                .ThenBy(f => f.ProductId)
                .ThenBy(f => f.Kind),
            "Revenue" => _sortDescending
                ? figures.OrderByDescending(f => f.TotalRevenue)
                : figures.OrderBy(f => f.TotalRevenue),
            _ => _sortDescending
                ? figures.OrderByDescending(f => f.TotalAmount)
                : figures.OrderBy(f => f.TotalAmount)
        }).ToList();

        private ISeries[] _pieSeries = Array.Empty<ISeries>();
        public ISeries[] PieSeries { get => _pieSeries; private set { _pieSeries = value; OnPropertyChanged(); } }

        // Doughnut hole radius (px). The view scales this with the chart size (see SetPieInnerRadius)
        // so the ring keeps the same proportion; new pie slices are built with the current value.
        private double _pieInnerRadius = 55;

        /// <summary>Sets the doughnut hole radius and updates the live pie slices (called by the view on resize).</summary>
        public void SetPieInnerRadius(double radius)
        {
            radius = Math.Max(6, radius);
            if (Math.Abs(radius - _pieInnerRadius) < 0.5) return;
            _pieInnerRadius = radius;
            foreach (var s in _pieSeries)
                if (s is PieSeries<double> pie) pie.InnerRadius = radius;
        }

        // Persistent series/axes instances: only their Values/Labels are updated on reload, so the
        // column chart animates smoothly instead of resetting to zero each time it is shown again.
        private readonly ColumnSeries<double> _timeCourseColumn;
        private readonly Axis _timeCourseXAxis = new() { TextSize = 12 };
        // Quantities are whole pieces: MinStep keeps LiveCharts from picking a fractional step for
        // small counts, which the integer labeler would otherwise print as repeated labels (0, 0, 1, 2, 2 …).
        private readonly Axis _timeCourseYAxis = new() { MinLimit = 0, MinStep = 1, TextSize = 12, Labeler = v => ((int)Math.Round(v)).ToString() };
        public ISeries[] TimeCourseSeries { get; }
        public Axis[] TimeCourseXAxes { get; }
        public Axis[] TimeCourseYAxes { get; }

        // ── Saved analyses ──
        public ObservableCollection<SavedAnalysisListItem> SavedAnalyses { get; }
        public bool HasSavedAnalyses => SavedAnalyses.Count > 0;

        private bool _hasActiveAnalysis;
        public bool HasActiveAnalysis { get => _hasActiveAnalysis; private set { _hasActiveAnalysis = value; OnPropertyChanged(); } }

        private string _activeAnalysisText = "Benutzerdefiniert";
        public string ActiveAnalysisText { get => _activeAnalysisText; private set { _activeAnalysisText = value; OnPropertyChanged(); } }

        // ── Commands ──
        public RelayCommand SelectQuickRangeCommand { get; }
        public RelayCommand SelectResolutionCommand { get; }
        public RelayCommand SetViewModeCommand { get; }
        public RelayCommand SelectAllProductsCommand { get; }
        public RelayCommand ClearProductsCommand { get; }
        public RelayCommand ExportCsvCommand { get; }
        public RelayCommand ExportPdfCommand { get; }
        public RelayCommand SortBreakdownCommand { get; }
        public RelayCommand ToggleRowCommand { get; }
        public RelayCommand SaveCurrentCommand { get; }
        public RelayCommand ApplySavedCommand { get; }
        public RelayCommand ManageCommand { get; }

        // ── Range selection ──
        private void SelectQuickRange(AnalysisSegmentItem item)
        {
            if (item == null) return;

            var today = DateTime.Today;
            (DateTime from, DateTime to) = (QuickRange)item.Value switch
            {
                QuickRange.Yesterday => (today.AddDays(-1), today.AddDays(-1).AddHours(23).AddMinutes(59)),
                QuickRange.Last7Days => (today.AddDays(-6), today.AddHours(23).AddMinutes(59)),
                _ => (today, today.AddHours(23).AddMinutes(59))
            };

            _suppressReload = true;
            SetRangeFields(from, to);
            foreach (var q in QuickRanges) q.IsSelected = ReferenceEquals(q, item);
            _suppressReload = false;

            Reload();
        }

        /// <summary>Editing a from/to field manually clears the quick-pick highlight (now a custom range).</summary>
        private void OnCustomEdited()
        {
            if (_suppressReload) return;
            foreach (var q in QuickRanges) q.IsSelected = false;
            Reload();
        }

        private void SetRangeFields(DateTime from, DateTime to)
        {
            _dateFrom = from.Date;
            _timeFrom = NearestTimeValue(TimeFrom_Values, from);
            _dateTo = to.Date;
            _timeTo = NearestTimeValue(TimeTo_Values, to);
            OnPropertyChanged(nameof(DateFrom));
            OnPropertyChanged(nameof(TimeFrom));
            OnPropertyChanged(nameof(DateTo));
            OnPropertyChanged(nameof(TimeTo));
        }

        private (DateTime from, DateTime to) EffectiveRange() => (
            new DateTime(_dateFrom.Year, _dateFrom.Month, _dateFrom.Day, _timeFrom.Hour, _timeFrom.Minute, 0),
            new DateTime(_dateTo.Year, _dateTo.Month, _dateTo.Day, _timeTo.Hour, _timeTo.Minute, _timeTo.Second));

        // ── View mode + resolution ──
        private void SelectViewMode(AnalysisSegmentItem item)
        {
            if (item == null) return;
            bool timeCourse = (string)item.Value == "TimeCourse";
            foreach (var v in ViewModes) v.IsSelected = ReferenceEquals(v, item);
            IsTimeCourse = timeCourse;
            IsBreakdown = !timeCourse;

            // The breakdown is always current (rebuilt on every reload); the time course is built
            // lazily, so catch up now if reloads happened while its tab was hidden.
            if (timeCourse && _timeCourseIsStale)
                BuildTimeCourse();
        }

        private void SelectResolution(AnalysisSegmentItem item)
        {
            if (item == null) return;
            _resolution = (AnalysisTimeResolution)item.Value;
            foreach (var r in Resolutions) r.IsSelected = ReferenceEquals(r, item);
            BuildTimeCourse();
        }

        // ── Product filter ──
        private void OnProductToggled()
        {
            if (_suppressReload) return;
            Reload();
        }

        private void SetAllProducts(bool selected)
        {
            _suppressReload = true;
            foreach (var p in ProductFilter) p.IsSelected = selected;
            _suppressReload = false;
            Reload();
        }

        private void UpdateFilteredProducts()
        {
            var search = _productSearchText?.Trim() ?? string.Empty;
            FilteredProductFilter = string.IsNullOrEmpty(search)
                ? ProductFilter.ToList()
                : ProductFilter.Where(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        // ── Core reload ──
        private void Reload()
        {
            if (_suppressReload) return;

            var (from, to) = EffectiveRange();
            _currentFrom = from;
            _currentTo = to;

            var (all, currentIds, includeFreeEntries) = CurrentSelection();
            int selectedCount = ProductFilter.Count(p => p.IsSelected);
            bool none = selectedCount == 0;

            ProductFilterSummary = none
                ? "keine Produkte"
                : all ? $"alle ({ProductFilter.Count})" : $"{selectedCount} von {ProductFilter.Count}";

            UpdateActiveMatch(all, currentIds, includeFreeEntries, from, to);

            if (none)
            {
                _currentFigures = new List<SalesFigure>();
                _priceDetailsByProduct = new Dictionary<int, IReadOnlyList<PriceDetailLine>>();
                _freeEntryGroups = Array.Empty<FreeEntryGroup>().ToLookup(g => g.ProductId);
                Breakdown = new ObservableCollection<AnalysisSalesRow>();
                TotalRevenue = 0m;
                TotalAmount = 0;
                PieSeries = Array.Empty<ISeries>();
                _timeCourseColumn.Values = Array.Empty<double>();
                _timeCourseIsStale = false; // an empty chart IS the current time course for "no products"
                return;
            }

            // All products: no id list, so products added later are part of it as well.
            bool allProducts = ProductFilter.Where(p => !p.IsFreeEntries).All(p => p.IsSelected);
            IReadOnlyCollection<int> ids = allProducts ? null : currentIds.ToList();

            _currentFigures = _analytics.GetSalesFigures(from, to, ids, includeFreeEntries)
                .Where(f => f.TotalAmount > 0)
                .ToList();

            _priceDetailsByProduct = _analytics.GetPricePeriods(from, to, ids)
                .GroupBy(p => p.ProductId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<PriceDetailLine>)g.Select(FormatPricePeriod).ToList());

            _freeEntryGroups = _analytics.GetFreeEntryGroups(from, to, ids, includeFreeEntries).ToLookup(g => g.ProductId);

            ApplyBreakdown();

            TotalRevenue = _currentFigures.Sum(f => f.TotalRevenue);
            TotalAmount = _currentFigures.Sum(f => f.TotalAmount);

            _currentIds = ids;
            _currentIncludeFreeEntries = includeFreeEntries;
            if (IsTimeCourse)
                BuildTimeCourse();
            else
                _timeCourseIsStale = true;
        }

        /// <summary>The filter as it is ticked: whether everything is selected (products and free
        /// entries), the selected product ids, and whether the free entries are included.</summary>
        private (bool all, HashSet<int> productIds, bool includeFreeEntries) CurrentSelection()
        {
            var selected = ProductFilter.Where(p => p.IsSelected).ToList();
            bool all = ProductFilter.Count > 0 && selected.Count == ProductFilter.Count;
            return (all,
                selected.Where(p => !p.IsFreeEntries).Select(p => p.ProductId).ToHashSet(),
                selected.Any(p => p.IsFreeEntries));
        }

        /// <summary>(Re)builds the table rows and the pie from the loaded figures in the chosen sort
        /// order, so a header click never hits the database.</summary>
        private void ApplyBreakdown()
        {
            var sorted = SortFigures(_currentFigures);

            var rows = new List<AnalysisSalesRow>();
            foreach (var f in sorted)
            {
                var fill = ColorBorderHelper.ParseBrush(ColorOf(f));
                bool isProduct = f.Kind == SalesFigureKind.Product;
                var row = new AnalysisSalesRow
                {
                    Kind = f.Kind,
                    ProductId = f.ProductId,
                    Name = NameOf(f),
                    Amount = f.TotalAmount,
                    Revenue = f.TotalRevenue,
                    UnitPrice = f.MinUnitPrice == f.MaxUnitPrice ? f.MinUnitPrice : null,
                    ColorBrush = fill,
                    ColorBorderBrush = ColorBorderHelper.Border(fill),
                    // A product's tooltip shows its own prices; special prices and free entries are
                    // listed when the row is expanded instead.
                    PriceDetails = isProduct && f.ProductId is int id && _priceDetailsByProduct.TryGetValue(id, out var details)
                        ? details
                        : Array.Empty<PriceDetailLine>(),
                    EnteredPrices = isProduct && f.VariablePriceAmount > 0 ? FormatEnteredPrices(f) : null,
                    Details = isProduct
                        ? Array.Empty<AnalysisDetailRow>()
                        : _freeEntryGroups[f.ProductId].Select(ToDetailRow).ToList()
                };
                row.IsExpanded = row.HasDetails && _expandedRows.Contains(row.Key);
                rows.Add(row);
            }

            Breakdown = new ObservableCollection<AnalysisSalesRow>(rows);
            PieSeries = BuildPie(sorted);
        }

        /// <summary>Expands a special price or free entries row into its details, or collapses it.</summary>
        private void ToggleRow(AnalysisSalesRow row)
        {
            if (row == null || !row.HasDetails) return;
            row.IsExpanded = !row.IsExpanded;
            if (row.IsExpanded) _expandedRows.Add(row.Key);
            else _expandedRows.Remove(row.Key);
        }

        /// <summary>The row's name: the product, "&lt;product&gt; (Sonderpreis)" or "Freie Eingabe".</summary>
        private static string NameOf(SalesFigure f) => f.Kind switch
        {
            SalesFigureKind.SpecialPrice => $"{f.ProductName} (Sonderpreis)",
            SalesFigureKind.FreeEntries => "Freie Eingabe",
            _ => f.ProductName
        };

        /// <summary>Special prices keep the product's colour; the free entries are grey, as at the till.</summary>
        private static string ColorOf(SalesFigure f) =>
            f.Kind == SalesFigureKind.FreeEntries ? ProductFilterItem.FreeEntriesColor : f.ProductColor;

        private static AnalysisDetailRow ToDetailRow(FreeEntryGroup g) => new()
        {
            Name = g.Note ?? "Ohne Angabe",
            IsWithoutDescription = g.Note == null,
            Amount = g.TotalAmount,
            UnitPrice = g.UnitPrice,
            Revenue = g.TotalRevenue
        };

        /// <summary>One tooltip line: "3,50 €" (or "ca. 3,50 €" for the suggestion of a variable price)
        /// plus "24.07.2023 – 01.08.2025" (a single date when the first and last sale of the segment
        /// share the day).</summary>
        private static PriceDetailLine FormatPricePeriod(ProductPricePeriod p)
        {
            string range = p.From.Date == p.To.Date
                ? p.From.ToString("dd.MM.yyyy", German)
                : $"{p.From.ToString("dd.MM.yyyy", German)} – {p.To.ToString("dd.MM.yyyy", German)}";
            string price = p.UnitPrice.ToString("C", German);
            return new PriceDetailLine(p.IsVariablePrice ? "ca. " + price : price, range);
        }

        /// <summary>The prices entered for a product with a variable price: "6,00 – 8,50 € · Ø 7,67 €",
        /// or just the price when all were the same.</summary>
        private static string FormatEnteredPrices(SalesFigure f)
        {
            if (f.VariablePriceMin == f.VariablePriceMax)
                return f.VariablePriceMin.ToString("C", German);

            decimal average = Math.Round(f.VariablePriceRevenue / f.VariablePriceAmount, 2);
            return $"{f.VariablePriceMin.ToString("N2", German)} – {f.VariablePriceMax.ToString("C", German)} · Ø {average.ToString("C", German)}";
        }

        private ISeries[] BuildPie(List<SalesFigure> figures)
        {
            bool byRevenue = _pieMetricColumn == "Revenue";
            var series = new List<ISeries>();
            foreach (var f in figures)
            {
                var pie = new PieSeries<double>
                {
                    Name = NameOf(f),
                    Values = new[] { byRevenue ? (double)f.TotalRevenue : f.TotalAmount },
                    InnerRadius = _pieInnerRadius,
                    Stroke = StrokePaint(ColorOf(f)),
                    DataLabelsPaint = null,
                    // The tooltip already shows the series Name; only add the metric value here.
                    ToolTipLabelFormatter = _ => byRevenue
                        ? f.TotalRevenue.ToString("C", German)
                        : f.TotalAmount.ToString()
                };

                // Fill from the same parsed brush as the table swatch (including its LightGray
                // fallback). Without an explicit fill, LiveCharts assigns an index-based palette
                // colour to products lacking a colour — which then changes on every re-sort.
                if (ColorBorderHelper.ParseBrush(ColorOf(f)) is System.Windows.Media.SolidColorBrush fillBrush)
                    pie.Fill = new SolidColorPaint(new SKColor(fillBrush.Color.R, fillBrush.Color.G, fillBrush.Color.B));

                series.Add(pie);
            }
            return series.ToArray();
        }

        private static SolidColorPaint StrokePaint(string color)
        {
            if (ColorBorderHelper.Border(color) is System.Windows.Media.SolidColorBrush b)
                return new SolidColorPaint(new SKColor(b.Color.R, b.Color.G, b.Color.B)) { StrokeThickness = 2 };
            return new SolidColorPaint(SKColors.Gray) { StrokeThickness = 2 };
        }

        private void BuildTimeCourse()
        {
            var (from, to) = (_currentFrom, _currentTo);
            var buckets = _analytics.GetSalesOverTime(from, to, _currentIds, _resolution, _currentIncludeFreeEntries);
            bool multiDay = (to - from).TotalDays > 1.0;

            // Day buckets carry no meaningful time-of-day, so label them by date only.
            string format = _resolution == AnalysisTimeResolution.Day ? "dd.MM" : (multiDay ? "dd.MM HH:mm" : "HH:mm");

            // Update the existing series/axis in place (no new instances) for smooth animation.
            _timeCourseColumn.Values = buckets.Select(b => (double)b.TotalAmount).ToArray();
            _timeCourseXAxis.Labels = buckets.Select(b => b.BucketStart.ToString(format)).ToArray();
            _timeCourseXAxis.LabelsRotation = buckets.Count > 12 ? 45 : 0;

            _timeCourseIsStale = false;
        }

        // ── Saved analyses ──
        private void LoadSavedAnalyses()
        {
            _savedCache = _savedAnalysisService.GetAll();
            _savedProductSets.Clear();
            SavedAnalyses.Clear();
            foreach (var a in _savedCache)
            {
                if (!a.AllProducts)
                    _savedProductSets[a.Id] = _savedAnalysisService.GetProductIds(a.Id).ToHashSet();
                SavedAnalyses.Add(new SavedAnalysisListItem
                {
                    Id = a.Id,
                    Name = a.Name,
                    Summary = SavedAnalysisManagementViewModel.Summarize(a)
                });
            }
            OnPropertyChanged(nameof(HasSavedAnalyses));
        }

        /// <summary>Lights up the matching saved analysis (if the current products + period equal a
        /// stored one), otherwise shows "Benutzerdefiniert". View mode is intentionally not compared.</summary>
        private void UpdateActiveMatch(bool all, HashSet<int> currentIds, bool includeFreeEntries, DateTime from, DateTime to)
        {
            string match = null;
            int? matchId = null;
            foreach (var a in _savedCache)
            {
                if (a.From != from || a.To != to) continue;
                if (a.AllProducts && all) { match = a.Name; matchId = a.Id; break; }
                if (!a.AllProducts && !all && a.IncludeFreeEntries == includeFreeEntries
                    && _savedProductSets.TryGetValue(a.Id, out var set) && set.SetEquals(currentIds))
                {
                    match = a.Name;
                    matchId = a.Id;
                    break;
                }
            }
            HasActiveAnalysis = match != null;
            ActiveAnalysisText = match ?? "Benutzerdefiniert";
            foreach (var item in SavedAnalyses) item.IsActive = item.Id == matchId;
        }

        private void RecomputeActive()
        {
            var (from, to) = EffectiveRange();
            var (all, productIds, includeFreeEntries) = CurrentSelection();
            UpdateActiveMatch(all, productIds, includeFreeEntries, from, to);
        }

        private SavedAnalysis BuildCurrentAnalysis(string name)
        {
            var (all, productIds, includeFreeEntries) = CurrentSelection();
            var (from, to) = EffectiveRange();
            return new SavedAnalysis
            {
                Name = name,
                AllProducts = all,
                ProductIds = all ? new List<int>() : productIds.ToList(),
                IncludeFreeEntries = all || includeFreeEntries,
                From = from,
                To = to
            };
        }

        /// <summary>Loads a saved analysis's products and period (NOT its view) into the current filter.</summary>
        private void ApplySaved(int id)
        {
            var a = _savedCache.FirstOrDefault(x => x.Id == id);
            if (a == null) return;

            _suppressReload = true;

            if (a.AllProducts)
            {
                foreach (var p in ProductFilter) p.IsSelected = true;
            }
            else
            {
                var set = _savedAnalysisService.GetProductIds(id).ToHashSet();
                foreach (var p in ProductFilter)
                    p.IsSelected = p.IsFreeEntries ? a.IncludeFreeEntries : set.Contains(p.ProductId);
            }

            if (a.From.HasValue && a.To.HasValue)
            {
                SetRangeFields(a.From.Value, a.To.Value);
                foreach (var q in QuickRanges) q.IsSelected = false;
            }

            _suppressReload = false;
            Reload(); // recomputes the active match, which now lights up this analysis
        }

        private async Task SaveCurrentAsync()
        {
            try
            {
                var name = await _dialogService.ShowTextInputAsync("Auswertung speichern", "Name", string.Empty);
                if (string.IsNullOrEmpty(name)) return;

                _savedAnalysisService.Add(BuildCurrentAnalysis(name));
                LoadSavedAnalyses();
                RecomputeActive();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save analysis");
                _dialogService.ShowError("Speichern fehlgeschlagen: " + ex.Message);
            }
        }

        private async Task ManageAsync()
        {
            var vm = new SavedAnalysisManagementViewModel(_savedAnalysisService, _productService);
            await _dialogService.ShowSavedAnalysisManagementAsync(vm);

            LoadSavedAnalyses();
            RecomputeActive();
        }

        // ── CSV export ──
        private void ExportCsv()
        {
            if (Breakdown.Count == 0)
            {
                _dialogService.ShowMessage("Keine Daten zum Exportieren.");
                return;
            }

            try
            {
                var sb = new StringBuilder();
                // Several prices leave the unit price cell empty, so spreadsheets keep the column numeric.
                sb.AppendLine("Produkt;Menge;Einzelpreis in €;Umsatz in €");
                foreach (var row in Breakdown)
                    sb.AppendLine($"{CsvEscape(row.Name)};{row.Amount};{row.UnitPrice?.ToString("0.00", German)};{row.Revenue.ToString("0.00", German)}");
                sb.AppendLine($"Gesamt;{TotalAmount};;{TotalRevenue.ToString("0.00", German)}");

                // The dialog suggests a name that is still free in its folder ("… (2).csv"); the user's
                // final choice always wins, an existing file gets the dialog's own overwrite prompt.
                var baseName = SanitizeFileName($"KerwaKasse Auswertung - {ActiveAnalysisText}");
                var path = _dialogService.ShowSaveFileDialog("CSV-Datei (*.csv)|*.csv", "Auswertung exportieren", baseName, ".csv");
                if (string.IsNullOrEmpty(path)) return;

                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));

                _logger.LogInformation("Analysis exported to CSV: {FilePath}", path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CSV export failed");
                _dialogService.ShowError("Export fehlgeschlagen: " + ex.Message);
            }
        }

        // ── PDF export (QuestPDF writes the .pdf file directly) ──

        /// <summary>Asks for a report title (pre-filled with the active analysis' name, or
        /// "Benutzerdefiniert") and, when there are special prices or free entries, whether to append
        /// them in detail; then exports the shown breakdown to a PDF via the save dialog and opens it.
        /// The chosen title is both the document's heading and the suggested file name.</summary>
        private async Task ExportPdfAsync()
        {
            if (Breakdown.Count == 0)
            {
                _dialogService.ShowMessage("Keine Daten zum Exportieren.");
                return;
            }

            var options = await _dialogService.ShowPdfExportAsync(ActiveAnalysisText, Breakdown.Any(r => r.HasDetails));
            if (options == null) return; // cancelled or left empty
            string title = options.Title;

            try
            {
                var baseName = SanitizeFileName($"KerwaKasse Auswertung - {title}");
                var path = _dialogService.ShowSaveFileDialog("PDF-Datei (*.pdf)|*.pdf", "Auswertung als PDF exportieren", baseName, ".pdf");
                if (string.IsNullOrEmpty(path)) return;

                AnalysisQuestPdfExporter.Export(BuildReport(title, options.IncludeDetails), path);
                _logger.LogInformation("Analysis exported to PDF: {FilePath}", path);

                // Open the fresh PDF in the default viewer right away — after "save as PDF" the
                // next step is almost always looking at it.
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PDF export failed");
                _dialogService.ShowError("Export fehlgeschlagen: " + ex.Message);
            }
        }

        /// <summary>Snapshot of the shown breakdown (current sort order) with the user-chosen title.</summary>
        private AnalysisReport BuildReport(string title, bool includeDetails)
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return new AnalysisReport
            {
                AnalysisName = title,
                Period = $"{_currentFrom.ToString("dd.MM.yyyy HH:mm", German)} – {_currentTo.ToString("dd.MM.yyyy HH:mm", German)} Uhr",
                ProductSummary = ProductFilterSummary,
                Rows = Breakdown.ToList(),
                IncludeDetails = includeDetails,
                TotalAmount = TotalAmount,
                TotalRevenue = TotalRevenue,
                AppVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "?"
            };
        }

        private static string CsvEscape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.Contains(';') || value.Contains('"') || value.Contains('\n'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, ' ');
            return name.Trim();
        }

        // ── Helpers ──
        private static int ToId(object o) => o is int i ? i : 0;

        private static DateTime TimeOfDay(int hour, int minute) =>
            new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, hour, minute, 0);

        private static DateTime NearestTimeValue(ObservableCollection<DateTime> values, DateTime time)
        {
            var target = time.TimeOfDay;
            return values.OrderBy(v => Math.Abs((v.TimeOfDay - target).Ticks)).First();
        }
    }
}
