using KerwaKasse.Helper;
using KerwaKasse.Core.Services;

namespace KerwaKasse.MVVM.ViewModel
{
    public class MainWindowViewModel : PropertyChangedBase
    {
        public RelayCommand OrderPanelViewCommand { get; set; }
        public RelayCommand ProductsViewCommand { get; set; }
        public RelayCommand OrderHistoryViewCommand { get; set; }
        public RelayCommand AnalysisViewCommand { get; set; }
        public RelayCommand InfoViewCommand { get; set; }

        public InfoViewModel InfoVM { get; }

        private readonly IDialogService _dialogService;
        private readonly IProductService _productService;
        private readonly IOrderService _orderService;
        private readonly ISettingsService _settingsService;
        private readonly IMenuService _menuService;
        private readonly IAnalyticsService _analyticsService;
        private readonly ISavedAnalysisService _savedAnalysisService;

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

        public MainWindowViewModel(IDialogService dialogService, IProductService productService, IOrderService orderService, ISettingsService settingsService, string dbFilePath, IMenuService menuService, IAnalyticsService analyticsService, ISavedAnalysisService savedAnalysisService)
        {
            _dialogService = dialogService;
            _productService = productService;
            _orderService = orderService;
            _settingsService = settingsService;
            _menuService = menuService;
            _analyticsService = analyticsService;
            _savedAnalysisService = savedAnalysisService;

            InfoVM = new InfoViewModel(dialogService, dbFilePath);

            OrderPanelViewCommand = new RelayCommand(o => CurrentView = new OrderPanelViewModel(_productService, _orderService, _settingsService, _dialogService));
            ProductsViewCommand = new RelayCommand(o => CurrentView = new ProductsViewModel(_productService, _menuService, _settingsService, _dialogService));
            OrderHistoryViewCommand = new RelayCommand(o => CurrentView = new OrderHistoryViewModel(_orderService, _productService));
            AnalysisViewCommand = new RelayCommand(o => CurrentView = new AnalysisViewModel(_analyticsService, _productService, _savedAnalysisService, _dialogService));
            InfoViewCommand = new RelayCommand(o => CurrentView = InfoVM);

            CurrentView = new OrderPanelViewModel(_productService, _orderService, _settingsService, _dialogService);
        }
    }
}
