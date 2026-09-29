using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.ViewModel;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using KerwaKasse.Core.Models;

namespace KerwaKasse.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private readonly IDialogService _dialogService;
    private readonly IProductService _productService;
    private readonly IOrderService _orderService;
    private readonly ISettingsService _settings;
    private readonly IMenuService _menuService;
    private readonly IAnalyticsService _analyticsService;
    private readonly ISavedAnalysisService _savedAnalysisService;
    private readonly IDatabaseBackupService _backupService;
    private readonly IUpdateService _updateService;
    private readonly IEventCatalog _eventCatalog;
    private readonly MainWindowViewModel _sut;

    public MainWindowViewModelTests()
    {
        _dialogService = Substitute.For<IDialogService>();
        _productService = Substitute.For<IProductService>();
        _orderService = Substitute.For<IOrderService>();
        _settings = Substitute.For<ISettingsService>();
        _menuService = Substitute.For<IMenuService>();
        _analyticsService = Substitute.For<IAnalyticsService>();
        _savedAnalysisService = Substitute.For<ISavedAnalysisService>();
        _backupService = Substitute.For<IDatabaseBackupService>();
        _updateService = Substitute.For<IUpdateService>();
        _eventCatalog = Substitute.For<IEventCatalog>();
        _eventCatalog.ActiveFilePath.Returns("test.db");
        _eventCatalog.GetAll().Returns(new List<EventDatabase> { new("test.db", "Kerwa", 0, 0, null, null) });

        _productService.GetAvailable().Returns(new List<Product>());
        _productService.GetAll().Returns(new List<Product>());
        _orderService.GetOrdersByDate(Arg.Any<DateTime>()).Returns(new List<Order>());
        _orderService.GetSalesFigures(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(new List<SalesFigure>());
        _menuService.GetAll().Returns(new List<Menu>());
        _savedAnalysisService.GetAll().Returns(new List<SavedAnalysis>());
        _analyticsService.GetSalesFigures(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<IReadOnlyCollection<int>>())
            .Returns(new List<SalesFigure>());

        _sut = new MainWindowViewModel(_dialogService, _productService, _orderService, _settings, "test.db", _menuService, _analyticsService, _savedAnalysisService, _backupService, _updateService, _eventCatalog, NullLoggerFactory.Instance);
    }

    [Fact]
    public void Constructor_SetsOrderPanelViewAsDefault()
    {
        Assert.IsType<OrderPanelViewModel>(_sut.CurrentView);
    }

    [Fact]
    public void InfoViewCommand_ReloadsEventKeyFigures()
    {
        _eventCatalog.ClearReceivedCalls();

        _sut.InfoViewCommand.Execute(null);

        _eventCatalog.Received(1).GetAll();
        Assert.Same(_sut.InfoVM, _sut.CurrentView);
    }

    [Fact]
    public void WindowTitle_SingleEvent_StaysPlain()
    {
        Assert.Equal("KerwaKasse", _sut.WindowTitle);
    }

    [Fact]
    public void WindowTitle_SeveralEvents_ShowsActiveEvent()
    {
        _eventCatalog.GetAll().Returns(new List<EventDatabase>
        {
            new("test.db", "Kerwa", 0, 0, null, null),
            new("herbst.db", "Herbstkerwa", 0, 0, null, null)
        });

        var sut = new MainWindowViewModel(_dialogService, _productService, _orderService, _settings, "test.db", _menuService, _analyticsService, _savedAnalysisService, _backupService, _updateService, _eventCatalog, NullLoggerFactory.Instance);

        Assert.Equal("KerwaKasse – Kerwa", sut.WindowTitle);
    }

    [Fact]
    public void OrderPanelViewCommand_SetsOrderPanelViewModel()
    {
        _sut.ProductsViewCommand.Execute(null); // switch away first
        _sut.OrderPanelViewCommand.Execute(null);

        Assert.IsType<OrderPanelViewModel>(_sut.CurrentView);
    }

    [Fact]
    public void ProductsViewCommand_SetsProductsViewModel()
    {
        _sut.ProductsViewCommand.Execute(null);

        Assert.IsType<ProductsViewModel>(_sut.CurrentView);
    }

    [Fact]
    public void OrderHistoryViewCommand_SetsOrderHistoryViewModel()
    {
        _sut.OrderHistoryViewCommand.Execute(null);

        Assert.IsType<OrderHistoryViewModel>(_sut.CurrentView);
    }

    [Fact]
    public void AnalysisViewCommand_SetsAnalysisViewModel()
    {
        _sut.AnalysisViewCommand.Execute(null);

        Assert.IsType<AnalysisViewModel>(_sut.CurrentView);
    }

    [Fact]
    public void InfoViewCommand_SetsInfoViewModel()
    {
        _sut.InfoViewCommand.Execute(null);

        Assert.IsType<InfoViewModel>(_sut.CurrentView);
    }

    [Fact]
    public void InfoVM_IsSameInstanceOnRepeatedNavigation()
    {
        _sut.InfoViewCommand.Execute(null);
        var first = _sut.CurrentView;
        _sut.OrderPanelViewCommand.Execute(null);
        _sut.InfoViewCommand.Execute(null);

        Assert.Same(first, _sut.CurrentView);
    }
}
