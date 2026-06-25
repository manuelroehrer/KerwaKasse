using System;
using System.Windows.Media;
using KerwaKasse.Helper;

namespace KerwaKasse.MVVM.Model
{
    /// <summary>A product in the analysis product filter: a checkbox with the product's colour swatch
    /// (plus a complementary border). Toggling notifies the view model so the analysis reloads.</summary>
    public class ProductFilterItem : PropertyChangedBase
    {
        private readonly Action _onChanged;

        public int ProductId { get; }
        public string Name { get; }
        public Brush ColorBrush { get; }
        public Brush ColorBorderBrush { get; }

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
        {
            ProductId = productId;
            Name = name;
            ColorBrush = ColorBorderHelper.ParseBrush(color ?? "#CCCCCC");
            ColorBorderBrush = ColorBorderHelper.Border(ColorBrush);
            _isSelected = isSelected;
            _onChanged = onChanged;
        }
    }
}
