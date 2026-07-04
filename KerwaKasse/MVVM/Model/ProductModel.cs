using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Media;
using System.Threading.Tasks;

namespace KerwaKasse.MVVM.Model
{
    public class ProductModel : PropertyChangedBase
    {
        private int productID;
        public int ProductID
        {
            get { return productID; }
            set
            {
                productID = value;
                OnPropertyChanged();
            }
        }

        private string description;
        public string Name
        {
            get { return description; }
            set
            {
                description = value;
                OnPropertyChanged();
            }
        }

        private decimal price;
        public decimal Price
        {
            get { return price; }
            set
            {
                price = value;
                OnPropertyChanged();
            }
        }

        private bool available;
        public bool Available
        {
            get { return available; }
            set
            {
                available = value;

                if (Available == true)
                    AvailableIcon = "\uE73E";
                else
                    AvailableIcon = "\uE711";

                OnPropertyChanged();
            }
        }

        private string availableIcon;
        public string AvailableIcon
        {
            get { return availableIcon; }
            set
            {
                availableIcon = value;
                OnPropertyChanged();
            }
        }

        private string colorAsString;
        public string ColorAsString
        {
            get
            {
                // Same fallback as ColorBorderHelper.ParseBrush (LightGray), so a product without a
                // colour looks identical on the order panel, in the analysis table and in the pie.
                if (colorAsString == null)
                    return "#D3D3D3";
                else
                    return colorAsString;
            }
            set
            {
                colorAsString = value;
                this.Color = ConvertFromString(ColorAsString);
                OnPropertyChanged();
            }
        }

        private Brush color;
        public Brush Color
        {
            get { return color; }
            set
            {
                color = value;
                OnPropertyChanged();
            }
        }

        private int positionNumber;
        public int PositionNumber
        {
            get { return positionNumber; }
            set
            {
                positionNumber = value;
                OnPropertyChanged();
            }
        }

        private static Brush ConvertFromString(string color)
        {
            Brush brush = (Brush)new BrushConverter().ConvertFrom(color);
            return brush;
        }

        public override string ToString()
        {
            return PositionNumber + ". " + Name;
        }
    }
}
