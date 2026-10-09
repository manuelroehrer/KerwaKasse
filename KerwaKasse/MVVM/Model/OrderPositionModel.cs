using KerwaKasse.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

        // Stored alongside the price, so an order read from the history is written back unchanged. Set
        // once when the position is created.
        public int? ProductId { get; set; }
        public OrderPositionKind Kind { get; set; }
        public decimal? ListPrice { get; set; }
        public string Note { get; set; }

        public OrderPositionModel()
        {
            Amount = 1;
        }
    }
}
