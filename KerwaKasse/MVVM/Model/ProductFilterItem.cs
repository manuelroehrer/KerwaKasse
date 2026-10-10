using System;
using System.Windows.Media;
using KerwaKasse.Helper;

namespace KerwaKasse.MVVM.Model
{
    /// <summary>A product in the analysis product filter: a checkbox with the product's colour swatch
    /// (plus a complementary border). Toggling notifies the view model so the analysis reloads. One
    /// extra entry, <see cref="IsFreeEntries"/>, stands for the free entries without product.</summary>
    public class ProductFilterItem : PropertyChangedBase
    {
        /// <summary>Grey of the free entries, as at the till and in the history.</summary>
        public const string FreeEntriesColor = "#9AA0A6";

        private readonly Action _onChanged;

        /// <summary>The product; 0 for the free entries entry.</summary>
        public int ProductId { get; }
        public string Name { get; }
        public Brush ColorBrush { get; }
        public Brush ColorBorderBrush { get; }

        /// <summary>True for the "Freie Eingabe" entry, which selects the free entries without product.</summary>
        public bool IsFreeEntries { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                    _onChanged?.Invoke();
                }
            }
        }

        public ProductFilterItem(int productId, string name, string color, bool isSelected, Action onChanged)
            : this(productId, name, color, isSelected, onChanged, isFreeEntries: false)
        {
        }

        private ProductFilterItem(int productId, string name, string color, bool isSelected, Action onChanged, bool isFreeEntries)
        {
            ProductId = productId;
            Name = name;
            ColorBrush = ColorBorderHelper.ParseBrush(color ?? "#CCCCCC");
            ColorBorderBrush = ColorBorderHelper.Border(ColorBrush);
            IsFreeEntries = isFreeEntries;
            _isSelected = isSelected;
            _onChanged = onChanged;
        }

        /// <summary>The "Freie Eingabe" entry of a product filter.</summary>
        public static ProductFilterItem FreeEntries(bool isSelected, Action onChanged) =>
            new(0, "Freie Eingabe", FreeEntriesColor, isSelected, onChanged, isFreeEntries: true);
    }
}
