using KerwaKasse.Helper;
using KerwaKasse.Core.Services;
using Microsoft.Extensions.Logging;
using System;

namespace KerwaKasse.MVVM.ViewModel
{
    public class MainWindowViewModel : PropertyChangedBase
    {
        public RelayCommand OrderPanelViewCommand { get; set; }
        public RelayCommand ProductsViewCommand { get; set; }
        public RelayCommand OrderHistoryViewCommand { get; set; }
        public RelayCommand AnalysisViewCommand { get; set; }
        public RelayCommand InfoViewCommand { get; set; }

        /// <summary>Raised after the user switched to another event. The window then rebuilds the
        /// services (and this view model) on top of the newly active database.</summary>
        public event EventHandler ActiveEventSwitched;

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

        private string _windowTitle = "KerwaKasse";
        /// <summary>"KerwaKasse – Herbstkerwa" as soon as there is more than one event, so nobody books
        /// into the wrong one by accident. With a single event the title stays plain "KerwaKasse".</summary>
        public string WindowTitle
        {
            get => _windowTitle;
            private set { _windowTitle = value; OnPropertyChanged(); }
        }

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

        public MainWindowViewModel(IDialogService dialogService, IProductService productService, IOrderService orderService, ISettingsService settingsService, string dataFolder, IMenuService menuService, IAnalyticsService analyticsService, ISavedAnalysisService savedAnalysisService, IDatabaseBackupService backupService, IUpdateService updateService, IEventCatalog eventCatalog, ILoggerFactory loggerFactory)
        {
            _dialogService = dialogService;
            _productService = productService;
            _orderService = orderService;
            _settingsService = settingsService;
            _menuService = menuService;
            _analyticsService = analyticsService;
            _savedAnalysisService = savedAnalysisService;
            _loggerFactory = loggerFactory;

            var events = new EventsViewModel(eventCatalog, backupService, dialogService, loggerFactory.CreateLogger<EventsViewModel>());
            events.ActiveEventSwitched += (_, _) => ActiveEventSwitched?.Invoke(this, EventArgs.Empty);
            events.EventsChanged += (_, _) => UpdateWindowTitle();
            InfoVM = new InfoViewModel(dialogService, dataFolder, backupService, updateService, settingsService, events, loggerFactory.CreateLogger<InfoViewModel>());

            OrderPanelViewCommand = new RelayCommand(o => CurrentView = CreateOrderPanelViewModel());
            ProductsViewCommand = new RelayCommand(o => CurrentView = new ProductsViewModel(_productService, _menuService, _settingsService, _dialogService));
            OrderHistoryViewCommand = new RelayCommand(o => CurrentView = new OrderHistoryViewModel(_orderService, _productService));
            AnalysisViewCommand = new RelayCommand(o => CurrentView = new AnalysisViewModel(_analyticsService, _productService, _savedAnalysisService, _dialogService, _loggerFactory.CreateLogger<AnalysisViewModel>()));
            // The info page shows the events' key figures, which the other pages change (products,
            // sales), so they are reloaded every time the page is shown.
            InfoViewCommand = new RelayCommand(o =>
            {
                InfoVM.Events.Refresh();
                CurrentView = InfoVM;
            });

            UpdateWindowTitle();
            CurrentView = CreateOrderPanelViewModel();
        }

        private void UpdateWindowTitle()
        {
            var events = InfoVM.Events;
            WindowTitle = events.Events.Count > 1 ? $"KerwaKasse – {events.ActiveEventName}" : "KerwaKasse";
        }

        private OrderPanelViewModel CreateOrderPanelViewModel() =>
            new(_productService, _orderService, _settingsService, _dialogService, _loggerFactory.CreateLogger<OrderPanelViewModel>());
    }
}
