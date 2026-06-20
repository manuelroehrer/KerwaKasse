using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.ViewModel;
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
    private readonly MainWindowViewModel _sut;

    public MainWindowViewModelTests()
    {
        _dialogService = Substitute.For<IDialogService>();
        _productService = Substitute.For<IProductService>();
        _orderService = Substitute.For<IOrderService>();
        _settings = Substitute.For<ISettingsService>();
        _menuService = Substitute.For<IMenuService>();

        _productService.GetAvailable().Returns(new List<Product>());
        _productService.GetAll().Returns(new List<Product>());
        _orderService.GetOrdersByDate(Arg.Any<DateTime>()).Returns(new List<Order>());
        _orderService.GetSalesFigures(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(new List<SalesFigure>());
        _menuService.GetAll().Returns(new List<Menu>());

        _sut = new MainWindowViewModel(_dialogService, _productService, _orderService, _settings, "test.db", _menuService);
    }

    [Fact]
    public void Constructor_SetsOrderPanelViewAsDefault()
    {
        Assert.IsType<OrderPanelViewModel>(_sut.CurrentView);
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
    public void StatisticsViewCommand_SetsStatisticsViewModel()
    {
        _sut.StatisticsViewCommand.Execute(null);

        Assert.IsType<StatisticsViewModel>(_sut.CurrentView);
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
