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
        private readonly string _origDescription;
        private readonly string _origPriceText;
        private readonly string _origColor;
        private readonly bool _origAvailable;

        // ── Name ────────────────────────────────────────────────
        private string _description;
        private bool _descriptionTouched;

        public string Description
        {
            get => _description;
            set
            {
                if (_description != value)
                    _descriptionTouched = true;
                _description = value;
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
            ? "Bezeichnung wieder sperren"
            : "Bezeichnung entsperren (nur für Korrekturen)";

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
                if (decimal.TryParse(_priceText, NumberStyles.Any, CultureInfo.CurrentCulture, out var p) && p >= 0)
                    return p;
                return null;
            }
        }

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
            _description != _origDescription ||
            _priceText != _origPriceText ||
            _available != _origAvailable ||
            !SameColor(_colorAsString, _origColor);

        /// <summary>True while the panel holds unsaved work: a new product, or edits to an existing one.</summary>
        public bool HasUnsavedChanges => IsNew || IsDirty;

        /// <summary>Caption for the unsaved-state chip in the panel header.</summary>
        public string UnsavedChipText => IsNew ? "Nicht gespeichert" : "Geändert";

        // ── Validation ──────────────────────────────────────────
        public const int MaxDescriptionLength = 40;

        /// <summary>
        /// True when the data is invalid (used by SaveDetailCommand CanExecute).
        /// Always checks raw state, ignoring touch-tracking.
        /// </summary>
        public bool HasErrors =>
            ParsedPrice == null ||
            string.IsNullOrWhiteSpace(_description) ||
            (_description?.Length ?? 0) > MaxDescriptionLength;

        // IDataErrorInfo
        string IDataErrorInfo.Error => string.Empty;

        public string this[string columnName]
        {
            get
            {
                if (columnName == nameof(PriceText))
                {
                    if (!decimal.TryParse(_priceText, NumberStyles.Any, CultureInfo.CurrentCulture, out var val))
                        return "Kein gültiger Preis";
                    if (val < 0)
                        return "Preis darf nicht negativ sein";
                }
                if (columnName == nameof(Description))
                {
                    if (!_descriptionTouched) return string.Empty;
                    if (string.IsNullOrWhiteSpace(_description))
                        return "Bezeichnung darf nicht leer sein";
                    if (_description.Length > MaxDescriptionLength)
                        return $"Maximal {MaxDescriptionLength} Zeichen erlaubt";
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

            _description = description;
            _descriptionTouched = !isNew;
            _priceText = price.ToString("F2", CultureInfo.CurrentCulture);
            _colorAsString = colorAsString;
            _available = available;

            _origDescription = _description;
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
