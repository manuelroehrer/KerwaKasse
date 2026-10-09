using KerwaKasse.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

namespace KerwaKasse.MVVM.Model
{
    public class OrderPositionModel : PropertyChangedBase
    {
        private int amount;
        public int Amount
        {
            get { return amount; }
            set
            {
                amount = value;
                OnPropertyChanged();
            }
        }

        private ProductModel product;
        public ProductModel Product
        {
            get { return product; }
            set
            {
                product = value;
                OnPropertyChanged();
            }
        }

        private decimal total;
        public decimal Total
        {
            get { return total; }
            set
            {
                total = value;
                OnPropertyChanged();
            }
        }

        private decimal unitPrice;
        /// <summary>The price charged per piece: the list price, an entered variable price or the amount
        /// of a free entry.</summary>
        public decimal UnitPrice
        {
            get { return unitPrice; }
            set
            {
                unitPrice = value;
                OnPropertyChanged();
            }
        }

        // Saved with the position; set once when it is created. Product is null for a free entry
        // without product.
        public int? ProductId { get; set; }
        public OrderPositionKind Kind { get; set; }
        public decimal? ListPrice { get; set; }
        public string Note { get; set; }

        /// <summary>The row's main line: the product, or for a free entry without product its description.</summary>
        public string DisplayName => !string.IsNullOrEmpty(Product?.Name) ? Product.Name : Note ?? "Freie Eingabe";

        /// <summary>The row's second line, only for free entries: the description of a special price
        /// ("Sonderpreis" without one), "Freie Eingabe" under a described entry, or "ohne Angabe".</summary>
        public string Subline => Kind != OrderPositionKind.FreeEntry ? null
            : ProductId != null ? Note ?? "Sonderpreis"
            : Note != null ? "Freie Eingabe" : "ohne Angabe";

        public bool HasSubline => Subline != null;

        /// <summary>A free entry added without description or product; highlighted so it gets noticed.</summary>
        public bool IsWithoutDescription => Kind == OrderPositionKind.FreeEntry && ProductId == null && Note == null;

        /// <summary>The colour strip: the product's colour, a neutral grey for a free entry without product.</summary>
        public Brush ColorBrush => Product?.Color ?? FreeEntryBrush;

        private static readonly Brush FreeEntryBrush = CreateFreeEntryBrush();

        private static Brush CreateFreeEntryBrush()
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x9A, 0xA0, 0xA6));
            brush.Freeze();
            return brush;
        }

        public OrderPositionModel()
        {
            Amount = 1;
        }
    }
}
