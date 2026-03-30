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

        public OrderPositionModel()
        {
            Amount = 1;
        }
    }
}
