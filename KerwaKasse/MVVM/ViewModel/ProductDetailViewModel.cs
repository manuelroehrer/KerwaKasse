using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;

namespace KerwaKasse.MVVM.ViewModel
{
    /// <summary>
    /// ViewModel for the right-side detail / edit panel in the product management view.
    /// Handles input validation and exposes editable state for a single product.
    /// Save / Discard / name-lock toggling are handled by the parent ProductsViewModel
    /// to keep persistence logic in one place.
    /// </summary>
    public class ProductDetailViewModel : PropertyChangedBase, IDataErrorInfo
    {
        public int ProductId { get; }
        public bool IsNew { get; }

        /// <summary>How many order positions reference this product (used in the unlock-name warning).</summary>
        public int UsageCount { get; }

        // Original values to detect whether anything actually changed.
        private readonly string _origName;
        private readonly string _origPriceText;
        private readonly string _origColor;
        private readonly bool _origAvailable;

        // ── Name ────────────────────────────────────────────────
        private string _name;
        private bool _nameTouched;

        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                    _nameTouched = true;
                _name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasErrors));
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        }

        private bool _nameUnlocked;
        /// <summary>Controls whether the description TextBox is editable. New products start unlocked.</summary>
        public bool NameUnlocked
        {
            get => _nameUnlocked;
            set
            {
                _nameUnlocked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LockGlyph));
                OnPropertyChanged(nameof(LockTooltip));
                OnPropertyChanged(nameof(NameHint));
                OnPropertyChanged(nameof(NameWarningVisible));
            }
        }

        /// <summary>The lock toggle is shown for existing products (new products are always editable).</summary>
        public bool UnlockButtonVisible => !IsNew;

        /// <summary>Segoe MDL2 glyph: open padlock (E785) when unlocked, closed padlock (E72E) when locked.</summary>
        public string LockGlyph => NameUnlocked ? ((char)0xE785).ToString() : ((char)0xE72E).ToString();

        public string LockTooltip => NameUnlocked
            ? "Name wieder sperren"
            : "Name entsperren (nur für Korrekturen)";

        /// <summary>Warning shown only while an existing product's name is unlocked.
        /// KerwaKasse is not a receipt system — only evaluations (not receipts) are affected.</summary>
        public string NameHint => "Name entsperrt — eine Umbenennung wirkt sich auf bestehende Auswertungen aus.";

        /// <summary>The rename warning is relevant only for existing products whose name is currently unlocked.</summary>
        public bool NameWarningVisible => !IsNew && NameUnlocked;

        // ── Price ───────────────────────────────────────────────
        private string _priceText;
        /// <summary>Raw text from the TextBox. Validated via IDataErrorInfo.</summary>
        public string PriceText
        {
            get => _priceText;
            set
            {
                _priceText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasErrors));
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        }

        public decimal? ParsedPrice
        {
            get
            {
                if (TryParsePrice(_priceText, out var p) && p >= 0 && decimal.Round(p, 2) == p)
                    return p;
                return null;
            }
        }

        /// <summary>
        /// Parses a price accepting both ',' and '.' as the decimal separator (touch keypads vary);
        /// '.' is never treated as a thousands separator.
        /// </summary>
        private static bool TryParsePrice(string text, out decimal value) =>
            decimal.TryParse(
                (text ?? string.Empty).Trim().Replace(',', '.'),
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out value);

        // ── Color ───────────────────────────────────────────────
        private string _colorAsString;
        public string ColorAsString
        {
            get => _colorAsString ?? "#CCCCCC";
            set
            {
                _colorAsString = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        }

        // ── Availability ────────────────────────────────────────
        private bool _available;
        public bool Available
        {
            get => _available;
            set
            {
                _available = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(AvailabilityText));
                OnPropertyChanged(nameof(IsDirty));
                OnPropertyChanged(nameof(HasUnsavedChanges));
            }
        }

        /// <summary>Caption next to the availability toggle; reflects the current state.</summary>
        public string AvailabilityText => _available ? "Verfügbar" : "Nicht verfügbar";

        // ── Dirty tracking ──────────────────────────────────────
        /// <summary>True when any editable field differs from the loaded values (drives Save enablement).</summary>
        public bool IsDirty =>
            _name != _origName ||
            _priceText != _origPriceText ||
            _available != _origAvailable ||
            !SameColor(_colorAsString, _origColor);

        /// <summary>True while the panel holds unsaved work: a new product, or edits to an existing one.</summary>
        public bool HasUnsavedChanges => IsNew || IsDirty;

        /// <summary>Caption for the unsaved-state chip in the panel header.</summary>
        public string UnsavedChipText => IsNew ? "Nicht gespeichert" : "Geändert";

        // ── Validation ──────────────────────────────────────────
        public const int MaxNameLength = 40;

        /// <summary>
        /// True when the data is invalid (used by SaveDetailCommand CanExecute).
        /// Always checks raw state, ignoring touch-tracking.
        /// </summary>
        public bool HasErrors =>
            ParsedPrice == null ||
            string.IsNullOrWhiteSpace(_name) ||
            (_name?.Length ?? 0) > MaxNameLength;

        // IDataErrorInfo
        string IDataErrorInfo.Error => string.Empty;

        public string this[string columnName]
        {
            get
            {
                if (columnName == nameof(PriceText))
                {
                    if (!TryParsePrice(_priceText, out var val))
                        return "Kein gültiger Preis";
                    if (val < 0)
                        return "Preis darf nicht negativ sein";
                    if (decimal.Round(val, 2) != val)
                        return "Höchstens zwei Nachkommastellen";
                }
                if (columnName == nameof(Name))
                {
                    if (!_nameTouched) return string.Empty;
                    if (string.IsNullOrWhiteSpace(_name))
                        return "Name darf nicht leer sein";
                    if (_name.Length > MaxNameLength)
                        return $"Maximal {MaxNameLength} Zeichen erlaubt";
                }
                return string.Empty;
            }
        }

        public ProductDetailViewModel(
            int productId,
            string description,
            decimal price,
            string colorAsString,
            bool available,
            int usageCount,
            bool isNew)
        {
            ProductId = productId;
            IsNew = isNew;
            UsageCount = usageCount;

            _name = description;
            _nameTouched = !isNew;
            _priceText = price.ToString("F2", CultureInfo.CurrentCulture);
            _colorAsString = colorAsString;
            _available = available;

            _origName = _name;
            _origPriceText = _priceText;
            _origColor = _colorAsString;
            _origAvailable = _available;

            // New products have their name field editable from the start
            _nameUnlocked = isNew;
        }

        /// <summary>Compares two colour strings by parsed value, so e.g. "#FF0000" and "#FFFF0000" count as equal.</summary>
        private static bool SameColor(string a, string b)
        {
            try
            {
                var ca = (Color)ColorConverter.ConvertFromString(a ?? "#CCCCCC");
                var cb = (Color)ColorConverter.ConvertFromString(b ?? "#CCCCCC");
                return ca == cb;
            }
            catch
            {
                return string.Equals(a, b);
            }
        }
    }
}
