using KerwaKasse.Core.Models;
using System.Windows.Media;

namespace KerwaKasse.MVVM.Model
{
    /// <summary>
    /// One editable position row in the order-history edit mode. Beyond the plain values it carries
    /// UI state so the view can highlight what the user changed: <see cref="IsNew"/> for freshly
    /// added positions, <see cref="IsRemoved"/> for ones marked for removal (shown greyed-out instead
    /// of vanishing) and <see cref="IsModified"/> when the amount, the price or what a free entry is
    /// for differs from the original.
    /// </summary>
    public class OrderHistoryEditPosition : PropertyChangedBase
    {
        public OrderPositionKind Kind { get; init; }

        private int? productId;
        /// <summary>Null for a free entry without product.</summary>
        public int? ProductId
        {
            get => productId;
            init => productId = value;
        }

        private string productName = string.Empty;
        public string ProductName
        {
            get => productName;
            init => productName = value;
        }

        private string note;
        public string Note
        {
            get => note;
            init => note = value;
        }

        private decimal? listPrice;
        public decimal? ListPrice
        {
            get => listPrice;
            init => listPrice = value;
        }

        private Brush colorBrush = Brushes.Transparent;
        /// <summary>The product's colour swatch (matches the colour shown in the products view).</summary>
        public Brush ColorBrush
        {
            get => colorBrush;
            init => colorBrush = value;
        }

        private decimal unitPrice;
        /// <summary>The price per piece; can be changed for variable prices and free entries.</summary>
        public decimal UnitPrice
        {
            get => unitPrice;
            set
            {
                unitPrice = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(Total));
                RaiseChangeFlags();
            }
        }

        // The values when editing started; the edit is compared against them.
        public int OriginalAmount { get; init; }
        public decimal OriginalUnitPrice { get; init; }
        public int? OriginalProductId { get; init; }
        public string OriginalNote { get; init; }

        /// <summary>True when this position was added during the current edit session.</summary>
        public bool IsNew { get; init; }

        /// <summary>Only an entered price can be changed afterwards; regular positions keep the list price.
        /// Nothing can be changed on a removed position.</summary>
        public bool CanEditPrice => Kind != OrderPositionKind.Regular && !IsRemoved;

        /// <summary>A free entry can get another description or product (unless it is removed).</summary>
        public bool CanEditPurpose => Kind == OrderPositionKind.FreeEntry && !IsRemoved;

        public string DisplayName => OrderPositionModel.DisplayNameOf(ProductName, Note);
        public string Subline => OrderPositionModel.SublineOf(Kind, ProductId);
        public bool HasSubline => Subline != null;
        public bool IsWithoutDescription => OrderPositionModel.IsWithoutDescriptionOf(Kind, ProductId, Note);

        /// <summary>Sets what a free entry is for: the product it is a special price for (or none) and
        /// the description (or none).</summary>
        public void SetPurpose(ProductModel product, string newNote)
        {
            bool productChanged = product?.ProductID != productId;
            productId = product?.ProductID;
            productName = product?.Name ?? string.Empty;
            colorBrush = product?.Color ?? OrderPositionModel.FreeEntryBrush;
            // The normal price the special price is compared with; kept while the product stays.
            if (productChanged)
                listPrice = product?.Price;
            note = string.IsNullOrWhiteSpace(newNote) ? null : newNote.Trim();

            OnPropertyChanged(nameof(ProductId));
            OnPropertyChanged(nameof(ProductName));
            OnPropertyChanged(nameof(ColorBrush));
            OnPropertyChanged(nameof(ListPrice));
            OnPropertyChanged(nameof(Note));
            OnPropertyChanged(nameof(DisplayName));
            OnPropertyChanged(nameof(Subline));
            OnPropertyChanged(nameof(HasSubline));
            OnPropertyChanged(nameof(IsWithoutDescription));
            RaiseChangeFlags();
        }

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
                RaiseChangeFlags();
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
                OnPropertyChanged(nameof(CanEditPrice));
                OnPropertyChanged(nameof(CanEditPurpose));
                OnPropertyChanged(nameof(Total));
                // IsModified depends on IsRemoved; without this the "changed" highlight would stick
                // when a previously-changed row is removed.
                RaiseChangeFlags();
            }
        }

        /// <summary>Inverse of <see cref="IsRemoved"/>; bound to the amount editor's IsEnabled.</summary>
        public bool IsActive => !IsRemoved;

        /// <summary>Line total; a removed position counts as 0.</summary>
        public decimal Total => IsRemoved ? 0m : Amount * UnitPrice;

        /// <summary>True when the amount, the price or what it is for was changed (and the row is
        /// neither new nor removed).</summary>
        public bool IsModified => !IsNew && !IsRemoved
                                  && (Amount != OriginalAmount || UnitPrice != OriginalUnitPrice
                                      || ProductId != OriginalProductId || Note != OriginalNote);

        /// <summary>True when the row differs from its original state in any way (drives highlighting).</summary>
        public bool IsChanged => IsNew || IsRemoved || IsModified;

        private void RaiseChangeFlags()
        {
            OnPropertyChanged(nameof(IsModified));
            OnPropertyChanged(nameof(IsChanged));
        }
    }
}
