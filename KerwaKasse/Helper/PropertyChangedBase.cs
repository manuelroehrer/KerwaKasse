using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace KerwaKasse
{
    //Basisklasse die das PropertyChanged Event für alle ViewModels/Models via Vererbung zur Verfügung stellt
    public class PropertyChangedBase : INotifyPropertyChanged
    {
        //PropertyChanged gibt dem Frontend bescheid, dass sich der inhalt einer gebindeten Property geändert hat
        public event PropertyChangedEventHandler PropertyChanged;

        //Methode, die in Setter aufgerufen werden kann, um event zu triggern
        public void OnPropertyChanged([CallerMemberName] string prop = null)
        {
            if (PropertyChanged != null)
            {
                //triggern des Events
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(prop));
            }
        }
    }
}
