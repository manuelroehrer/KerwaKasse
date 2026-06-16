using KerwaKasse.Helper;
using KerwaKasse.Core.Services;

namespace KerwaKasse.MVVM.ViewModel
{
    public class MainWindowViewModel : PropertyChangedBase
    {
        public RelayCommand OrderPanelViewCommand { get; set; }
        public RelayCommand ProductsViewCommand { get; set; }
        public RelayCommand HistoryViewCommand { get; set; }
        public RelayCommand StatisticsViewCommand { get; set; }
        public RelayCommand InfoViewCommand { get; set; }

        public InfoViewModel InfoVM { get; }

        private readonly IDialogService _dialogService;
        private readonly IProductService _productService;
        private readonly IOrderService _orderService;
        private readonly ISettingsService _settingsService;
        private readonly IMenuService _menuService;

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

        public MainWindowViewModel(IDialogService dialogService, IProductService productService, IOrderService orderService, ISettingsService settingsService, string dbFilePath, IMenuService menuService)
        {
            _dialogService = dialogService;
            _productService = productService;
            _orderService = orderService;
            _settingsService = settingsService;
            _menuService = menuService;

            InfoVM = new InfoViewModel(dialogService, dbFilePath);

            OrderPanelViewCommand = new RelayCommand(o => CurrentView = new OrderPanelViewModel(_productService, _orderService, _settingsService, _dialogService));
            ProductsViewCommand = new RelayCommand(o => CurrentView = new ProductsViewModel(_productService, _menuService, _settingsService, _dialogService));
            HistoryViewCommand = new RelayCommand(o => CurrentView = new HistoryViewModel(_orderService));
            StatisticsViewCommand = new RelayCommand(o => CurrentView = new StatisticsViewModel(_orderService));
            InfoViewCommand = new RelayCommand(o => CurrentView = InfoVM);

            CurrentView = new OrderPanelViewModel(_productService, _orderService, _settingsService, _dialogService);
        }
    }
}
