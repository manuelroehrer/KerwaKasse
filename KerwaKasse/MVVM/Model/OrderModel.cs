using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KerwaKasse.MVVM.Model
{
    public class OrderModel : PropertyChangedBase
    {
        private int orderID;
        public int OrderID
        {
            get { return orderID; }
            set
            {
                orderID = value;
                OnPropertyChanged();
            }
        }

        private string shortDescription;
        public string ShortDescription
        {
            get { return shortDescription; }
            set
            {
                shortDescription = value;
                OnPropertyChanged();
            }
        }

        private DateTime orderTime;
        public DateTime OrderTime
        {
            get { return orderTime; }
            set
            {
                orderTime = value;
                OnPropertyChanged();
            }
        }

        private List<OrderPositionModel> orderPositions;
        public List<OrderPositionModel> OrderPositions
        {
            get { return orderPositions; }
            set
            {
                orderPositions = value;
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

        public void UpdateDescAndTotal()
        {
            decimal calc = 0.0m;
            foreach(OrderPositionModel orderPos in OrderPositions)
            {
                orderPos.Total = orderPos.Amount * orderPos.UnitPrice;
                calc += orderPos.Total;
            }
            Total = calc;

            ShortDescription = string.Join(", ", OrderPositions.Select(p => p.DisplayName));
        }

        public OrderModel()
        {
            OrderPositions = new List<OrderPositionModel>();
            ShortDescription = "";
        }

        public override string ToString()
        {
            return "Order " + OrderTime.ToString() + ": " + OrderPositions.Count + " products";
        }
    }
}
