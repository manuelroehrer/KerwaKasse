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

        /// <summary>The row's main line, what was sold: the product (with the description of a special
        /// price), the description of a free entry, or "Ohne Angabe" for one without either.</summary>
        public string DisplayName => DisplayNameOf(Product?.Name, Note);

        /// <summary>The row's second line, the kind of position for everything but a regular one:
        /// "Variabler Preis", "Sonderpreis" or "Freie Eingabe".</summary>
        public string Subline => SublineOf(Kind, ProductId);

        public bool HasSubline => Subline != null;

        /// <summary>A free entry added without description or product; highlighted so it gets noticed.</summary>
        public bool IsWithoutDescription => IsWithoutDescriptionOf(Kind, ProductId, Note);

        /// <summary>The colour strip: the product's colour, a neutral grey for a free entry without product.</summary>
        public Brush ColorBrush => Product?.Color ?? FreeEntryBrush;

        // Shared with the history's edit rows, so a position reads the same at the till and in the history.
        public static string DisplayNameOf(string productName, string note) =>
            string.IsNullOrEmpty(productName) ? note ?? "Ohne Angabe"
            : note == null ? productName
            : $"{productName} · {note}";

        public static string SublineOf(OrderPositionKind kind, int? productId) => kind switch
        {
            OrderPositionKind.VariablePrice => "Variabler Preis",
            OrderPositionKind.FreeEntry => productId != null ? "Sonderpreis" : "Freie Eingabe",
            _ => null
        };

        public static bool IsWithoutDescriptionOf(OrderPositionKind kind, int? productId, string note) =>
            kind == OrderPositionKind.FreeEntry && productId == null && note == null;

        public static readonly Brush FreeEntryBrush = CreateFreeEntryBrush();

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
