using KerwaKasse.Helper;
using KerwaKasse.Core.Services;

namespace KerwaKasse.MVVM.ViewModel
{
    public class MainWindowViewModel : PropertyChangedBase
    {
        public RelayCommand HomeViewCommand { get; set; }
        public RelayCommand ProductsViewCommand { get; set; }
        public RelayCommand HistoryViewCommand { get; set; }
        public RelayCommand StatisticsViewCommand { get; set; }
        public RelayCommand DatabaseViewCommand { get; set; }

        public DatabaseViewModel DatabaseVM { get; }

        private readonly IDialogService _dialogService;
        private readonly IProductService _productService;
        private readonly IOrderService _orderService;
        private readonly ISettingsService _settingsService;

        private object currentView;
        public object CurrentView
        {
            get { return currentView; }
            set
            {
                currentView = value;
                OnPropertyChanged();
            }
        }

        public MainWindowViewModel(IDialogService dialogService, IProductService productService, IOrderService orderService, ISettingsService settingsService, string dbFilePath)
        {
            _dialogService = dialogService;
            _productService = productService;
            _orderService = orderService;
            _settingsService = settingsService;

            DatabaseVM = new DatabaseViewModel(dialogService, dbFilePath);

            HomeViewCommand = new RelayCommand(o => CurrentView = new HomeViewModel(_productService, _orderService, _settingsService, _dialogService));
            ProductsViewCommand = new RelayCommand(o => CurrentView = new ProductsViewModel(_productService, _settingsService));
            HistoryViewCommand = new RelayCommand(o => CurrentView = new HistoryViewModel(_orderService));
            StatisticsViewCommand = new RelayCommand(o => CurrentView = new StatisticsViewModel(_orderService));
            DatabaseViewCommand = new RelayCommand(o => CurrentView = DatabaseVM);

            CurrentView = new HomeViewModel(_productService, _orderService, _settingsService, _dialogService);
        }
    }
}
