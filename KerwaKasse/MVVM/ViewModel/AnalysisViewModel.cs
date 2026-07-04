using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
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
        private DateTime _currentFrom;
        private DateTime _currentTo;
        private IReadOnlyCollection<int> _currentIds;
        private List<SavedAnalysis> _savedCache = new();
        private readonly Dictionary<int, HashSet<int>> _savedProductSets = new();

        // Loaded figures, kept so re-sorting the table only rebuilds rows/pie without hitting the
        // database again.
        private List<SalesFigure> _currentFigures = new();
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
                new("Zeitverlauf", "Course")
            };

            _courseColumn = new ColumnSeries<double>
            {
                Name = "Menge",
                Values = Array.Empty<double>(),
                Fill = new SolidColorPaint(AccentColor)
            };
            CourseSeries = new ISeries[] { _courseColumn };
            CourseXAxes = new[] { _courseXAxis };
            CourseYAxes = new[] { _courseYAxis };

            ProductFilter = new ObservableCollection<ProductFilterItem>();
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
            SortBreakdownCommand = new RelayCommand(o => SortBreakdown(o as string));
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

        private bool _isCourse;
        public bool IsCourse { get => _isCourse; private set { _isCourse = value; OnPropertyChanged(); } }

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
            "Name" => _sortDescending
                ? figures.OrderByDescending(f => f.ProductName, StringComparer.CurrentCultureIgnoreCase)
                : figures.OrderBy(f => f.ProductName, StringComparer.CurrentCultureIgnoreCase),
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
        private readonly ColumnSeries<double> _courseColumn;
        private readonly Axis _courseXAxis = new() { TextSize = 12 };
        private readonly Axis _courseYAxis = new() { MinLimit = 0, TextSize = 12, Labeler = v => ((int)Math.Round(v)).ToString() };
        public ISeries[] CourseSeries { get; }
        public Axis[] CourseXAxes { get; }
        public Axis[] CourseYAxes { get; }

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
        public RelayCommand SortBreakdownCommand { get; }
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
            bool course = (string)item.Value == "Course";
            foreach (var v in ViewModes) v.IsSelected = ReferenceEquals(v, item);
            IsCourse = course;
            IsBreakdown = !course;
            // No reload: the data for both views is already current, so this is a pure visibility switch.
        }

        private void SelectResolution(AnalysisSegmentItem item)
        {
            if (item == null) return;
            _resolution = (AnalysisTimeResolution)item.Value;
            foreach (var r in Resolutions) r.IsSelected = ReferenceEquals(r, item);
            BuildCourse(_currentFrom, _currentTo, _currentIds);
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

            var selected = ProductFilter.Where(p => p.IsSelected).ToList();
            bool all = ProductFilter.Count > 0 && selected.Count == ProductFilter.Count;
            bool none = selected.Count == 0;
            var currentIds = selected.Select(p => p.ProductId).ToHashSet();

            ProductFilterSummary = none
                ? "keine Produkte"
                : all ? $"alle ({ProductFilter.Count})" : $"{selected.Count} von {ProductFilter.Count}";

            UpdateActiveMatch(all, currentIds, from, to);

            if (none)
            {
                _currentFigures = new List<SalesFigure>();
                Breakdown = new ObservableCollection<AnalysisSalesRow>();
                TotalRevenue = 0m;
                TotalAmount = 0;
                PieSeries = Array.Empty<ISeries>();
                _courseColumn.Values = Array.Empty<double>();
                return;
            }

            IReadOnlyCollection<int> ids = all ? null : currentIds.ToList();

            _currentFigures = _analytics.GetSalesFigures(from, to, ids)
                .Where(f => f.TotalAmount > 0)
                .ToList();

            ApplyBreakdown();

            TotalRevenue = _currentFigures.Sum(f => f.TotalRevenue);
            TotalAmount = _currentFigures.Sum(f => f.TotalAmount);

            // Build both views' data on every data change, so toggling the view is a pure visibility
            // switch (no re-initialisation flash on the pie / column chart when switching back).
            _currentIds = ids;
            BuildCourse(from, to, ids);
        }

        /// <summary>(Re)builds the table rows and the pie from the loaded figures in the chosen sort
        /// order, so a header click never hits the database.</summary>
        private void ApplyBreakdown()
        {
            var sorted = SortFigures(_currentFigures);

            var rows = new List<AnalysisSalesRow>();
            foreach (var f in sorted)
            {
                var fill = ColorBorderHelper.ParseBrush(f.ProductColor);
                rows.Add(new AnalysisSalesRow
                {
                    Name = f.ProductName,
                    Amount = f.TotalAmount,
                    Revenue = f.TotalRevenue,
                    ColorBrush = fill,
                    ColorBorderBrush = ColorBorderHelper.Border(fill)
                });
            }

            Breakdown = new ObservableCollection<AnalysisSalesRow>(rows);
            PieSeries = BuildPie(sorted);
        }

        private ISeries[] BuildPie(List<SalesFigure> figures)
        {
            bool byRevenue = _pieMetricColumn == "Revenue";
            var series = new List<ISeries>();
            foreach (var f in figures)
            {
                var pie = new PieSeries<double>
                {
                    Name = f.ProductName,
                    Values = new[] { byRevenue ? (double)f.TotalRevenue : f.TotalAmount },
                    InnerRadius = _pieInnerRadius,
                    Stroke = StrokePaint(f.ProductColor),
                    DataLabelsPaint = null,
                    // The tooltip already shows the series Name; only add the metric value here.
                    ToolTipLabelFormatter = _ => byRevenue
                        ? f.TotalRevenue.ToString("C", German)
                        : f.TotalAmount.ToString()
                };

                // Fill from the same parsed brush as the table swatch (including its LightGray
                // fallback). Without an explicit fill, LiveCharts assigns an index-based palette
                // colour to products lacking a colour — which then changes on every re-sort.
                if (ColorBorderHelper.ParseBrush(f.ProductColor) is System.Windows.Media.SolidColorBrush fillBrush)
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

        private void BuildCourse(DateTime from, DateTime to, IReadOnlyCollection<int> ids)
        {
            var buckets = _analytics.GetSalesOverTime(from, to, ids, _resolution);
            bool multiDay = (to - from).TotalDays > 1.0;

            // Day buckets carry no meaningful time-of-day, so label them by date only.
            string format = _resolution == AnalysisTimeResolution.Day ? "dd.MM" : (multiDay ? "dd.MM HH:mm" : "HH:mm");

            // Update the existing series/axis in place (no new instances) for smooth animation.
            _courseColumn.Values = buckets.Select(b => (double)b.TotalAmount).ToArray();
            _courseXAxis.Labels = buckets.Select(b => b.BucketStart.ToString(format)).ToArray();
            _courseXAxis.LabelsRotation = buckets.Count > 12 ? 45 : 0;
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
        private void UpdateActiveMatch(bool all, HashSet<int> currentIds, DateTime from, DateTime to)
        {
            string match = null;
            int? matchId = null;
            foreach (var a in _savedCache)
            {
                if (a.From != from || a.To != to) continue;
                if (a.AllProducts && all) { match = a.Name; matchId = a.Id; break; }
                if (!a.AllProducts && !all
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
            var selected = ProductFilter.Where(p => p.IsSelected).ToList();
            bool all = ProductFilter.Count > 0 && selected.Count == ProductFilter.Count;
            UpdateActiveMatch(all, selected.Select(p => p.ProductId).ToHashSet(), from, to);
        }

        private SavedAnalysis BuildCurrentAnalysis(string name)
        {
            var selected = ProductFilter.Where(p => p.IsSelected).ToList();
            bool all = ProductFilter.Count > 0 && selected.Count == ProductFilter.Count;
            var (from, to) = EffectiveRange();
            return new SavedAnalysis
            {
                Name = name,
                AllProducts = all,
                ProductIds = all ? new List<int>() : selected.Select(p => p.ProductId).ToList(),
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
                foreach (var p in ProductFilter) p.IsSelected = set.Contains(p.ProductId);
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
                sb.AppendLine("Produkt;Menge;Umsatz");
                foreach (var row in Breakdown)
                    sb.AppendLine($"{CsvEscape(row.Name)};{row.Amount};{row.Revenue.ToString("0.00", German)}");
                sb.AppendLine($"Gesamt;{TotalAmount};{TotalRevenue.ToString("0.00", German)}");

                // Suggest a non-colliding name up front: pick a free "… (2).csv" within the target
                // folder and offer it as the default. The user's final choice always wins — if they
                // deliberately keep an existing name, the dialog's own overwrite prompt handles it.
                var dir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var baseName = SanitizeFileName($"KerwaKasse Auswertung - {ActiveAnalysisText}");
                var suggestedName = UniqueFileName(dir, baseName, ".csv");

                var path = _dialogService.ShowSaveFileDialog("CSV-Datei (*.csv)|*.csv", "Auswertung exportieren", suggestedName, dir);
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

        /// <summary>A file name (no path) that does not yet exist in <paramref name="dir"/>:
        /// "<paramref name="baseName"/><paramref name="ext"/>", else " (2)", " (3)", … appended.</summary>
        private static string UniqueFileName(string dir, string baseName, string ext)
        {
            var candidate = baseName + ext;
            int n = 2;
            while (File.Exists(Path.Combine(dir, candidate)))
                candidate = $"{baseName} ({n++}){ext}";
            return candidate;
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
