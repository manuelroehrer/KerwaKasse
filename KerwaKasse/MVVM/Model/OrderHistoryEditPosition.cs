using System.Windows.Media;

namespace KerwaKasse.MVVM.Model
{
    /// <summary>
    /// One editable position row in the order-history edit mode. Beyond the plain values it carries
    /// UI state so the view can highlight what the user changed: <see cref="IsNew"/> for freshly
    /// added positions, <see cref="IsRemoved"/> for ones marked for removal (shown greyed-out instead
    /// of vanishing) and <see cref="IsAmountChanged"/> when the amount differs from the original.
    /// </summary>
    public class OrderHistoryEditPosition : PropertyChangedBase
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public decimal UnitPrice { get; init; }

        /// <summary>The product's colour swatch (matches the colour shown in the products view).</summary>
        public Brush ColorBrush { get; init; } = Brushes.Transparent;

        /// <summary>The amount this position had when editing started; 0 for newly added positions.</summary>
        public int OriginalAmount { get; init; }

        /// <summary>True when this position was added during the current edit session.</summary>
        public bool IsNew { get; init; }

        private int amount;
        public int Amount
        {
            get => amount;
            set
            {
                // Clamp so keyboard entry can never leave the valid 1..100 range.
                var clamped = value < 1 ? 1 : value > 100 ? 100 : value;
                amount = clamped;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Total));
                OnPropertyChanged(nameof(IsAmountChanged));
                OnPropertyChanged(nameof(IsChanged));
            }
        }

        private bool isRemoved;
        public bool IsRemoved
        {
            get => isRemoved;
            set
            {
                isRemoved = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsActive));
                OnPropertyChanged(nameof(Total));
                OnPropertyChanged(nameof(IsChanged));
                // IsAmountChanged depends on IsRemoved; without this the "changed" highlight would
                // stick when a previously-changed row is removed.
                OnPropertyChanged(nameof(IsAmountChanged));
            }
        }

        /// <summary>Inverse of <see cref="IsRemoved"/>; bound to the amount editor's IsEnabled.</summary>
        public bool IsActive => !IsRemoved;

        /// <summary>Line total; a removed position counts as 0.</summary>
        public decimal Total => IsRemoved ? 0m : Amount * UnitPrice;

        /// <summary>True when the amount was changed (and the row is neither new nor removed).</summary>
        public bool IsAmountChanged => !IsNew && !IsRemoved && Amount != OriginalAmount;

        /// <summary>True when the row differs from its original state in any way (drives highlighting).</summary>
        public bool IsChanged => IsNew || IsRemoved || IsAmountChanged;
    }
}
