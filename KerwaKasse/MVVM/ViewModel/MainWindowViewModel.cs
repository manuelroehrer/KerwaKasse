using KerwaKasse.Helper;
using KerwaKasse.Core.Services;
using Microsoft.Extensions.Logging;

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

        // Factory instead of individual loggers because the sub view models are created lazily on
        // every navigation, each with its own logger category.
        private readonly ILoggerFactory _loggerFactory;

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

        public MainWindowViewModel(IDialogService dialogService, IProductService productService, IOrderService orderService, ISettingsService settingsService, string dbFilePath, IMenuService menuService, IAnalyticsService analyticsService, ISavedAnalysisService savedAnalysisService, IDatabaseBackupService backupService, IUpdateService updateService, ILoggerFactory loggerFactory)
        {
            _dialogService = dialogService;
            _productService = productService;
            _orderService = orderService;
            _settingsService = settingsService;
            _menuService = menuService;
            _analyticsService = analyticsService;
            _savedAnalysisService = savedAnalysisService;
            _loggerFactory = loggerFactory;

            InfoVM = new InfoViewModel(dialogService, dbFilePath, backupService, updateService, settingsService, loggerFactory.CreateLogger<InfoViewModel>());

            OrderPanelViewCommand = new RelayCommand(o => CurrentView = CreateOrderPanelViewModel());
            ProductsViewCommand = new RelayCommand(o => CurrentView = new ProductsViewModel(_productService, _menuService, _settingsService, _dialogService));
            OrderHistoryViewCommand = new RelayCommand(o => CurrentView = new OrderHistoryViewModel(_orderService, _productService));
            AnalysisViewCommand = new RelayCommand(o => CurrentView = new AnalysisViewModel(_analyticsService, _productService, _savedAnalysisService, _dialogService, _loggerFactory.CreateLogger<AnalysisViewModel>()));
            InfoViewCommand = new RelayCommand(o => CurrentView = InfoVM);

            CurrentView = CreateOrderPanelViewModel();
        }

        private OrderPanelViewModel CreateOrderPanelViewModel() =>
            new(_productService, _orderService, _settingsService, _dialogService, _loggerFactory.CreateLogger<OrderPanelViewModel>());
    }
}
