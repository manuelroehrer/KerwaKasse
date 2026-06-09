using System;
using System.Windows.Media;

namespace KerwaKasse.MVVM.ViewModel
{
    /// <summary>
    /// ViewModel for a single product row in the product list view.
    /// The Available toggle writes through immediately to the database via callback.
    /// </summary>
    public class ProductListItemViewModel : PropertyChangedBase
    {
        private readonly Action<int, bool> _onAvailableChanged;

        public int ProductId { get; }
        public string Name { get; }
        public decimal Price { get; }
        public string ColorAsString { get; }
        public Brush ColorBrush { get; }

        private bool _available;
        public bool Available
        {
            get => _available;
            set
            {
                if (_available != value)
                {
                    _available = value;
                    OnPropertyChanged();
                    _onAvailableChanged?.Invoke(ProductId, value);
                }
            }
        }

        public ProductListItemViewModel(
            int productId,
            string description,
            decimal price,
            string colorAsString,
            bool available,
            Action<int, bool> onAvailableChanged)
        {
            ProductId = productId;
            Name = description;
            Price = price;
            ColorAsString = colorAsString ?? "#CCCCCC";
            ColorBrush = ParseBrush(ColorAsString);
            _available = available;
            _onAvailableChanged = onAvailableChanged;
        }

        private static Brush ParseBrush(string color)
        {
            try
            {
                return (Brush)new BrushConverter().ConvertFrom(color);
            }
            catch
            {
                return Brushes.LightGray;
            }
        }
    }
}
