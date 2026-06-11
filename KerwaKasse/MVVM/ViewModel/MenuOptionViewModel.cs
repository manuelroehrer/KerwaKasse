using KerwaKasse.Core.Models;

namespace KerwaKasse.MVVM.ViewModel
{
    /// <summary>
    /// One Speisekarte entry in the ProductsView dropdown: the menu plus whether it is the
    /// currently active card (so the row can highlight it).
    /// </summary>
    public class MenuOptionViewModel : PropertyChangedBase
    {
        public Menu Menu { get; }
        public string Name => Menu.Name;

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { _isActive = value; OnPropertyChanged(); }
        }

        public MenuOptionViewModel(Menu menu, bool isActive)
        {
            Menu = menu;
            _isActive = isActive;
        }
    }
}
