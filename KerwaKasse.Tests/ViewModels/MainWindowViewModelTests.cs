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
    private readonly MainWindowViewModel _sut;

    public MainWindowViewModelTests()
    {
        _dialogService = Substitute.For<IDialogService>();
        _productService = Substitute.For<IProductService>();
        _orderService = Substitute.For<IOrderService>();
        _settings = Substitute.For<ISettingsService>();

        _productService.GetAvailable().Returns(new List<Product>());
        _productService.GetAll().Returns(new List<Product>());
        _orderService.GetOrdersByDate(Arg.Any<DateTime>()).Returns(new List<Order>());
        _orderService.GetSalesFigures(Arg.Any<DateTime>(), Arg.Any<DateTime>()).Returns(new List<SalesFigure>());

        _sut = new MainWindowViewModel(_dialogService, _productService, _orderService, _settings, "test.db");
    }

    [Fact]
    public void Constructor_SetsHomeViewAsDefault()
    {
        Assert.IsType<HomeViewModel>(_sut.CurrentView);
    }

    [Fact]
    public void HomeViewCommand_SetsHomeViewModel()
    {
        _sut.ProductsViewCommand.Execute(null); // switch away first
        _sut.HomeViewCommand.Execute(null);

        Assert.IsType<HomeViewModel>(_sut.CurrentView);
    }

    [Fact]
    public void ProductsViewCommand_SetsProductsViewModel()
    {
        _sut.ProductsViewCommand.Execute(null);

        Assert.IsType<ProductsViewModel>(_sut.CurrentView);
    }

    [Fact]
    public void HistoryViewCommand_SetsHistoryViewModel()
    {
        _sut.HistoryViewCommand.Execute(null);

        Assert.IsType<HistoryViewModel>(_sut.CurrentView);
    }

    [Fact]
    public void StatisticsViewCommand_SetsStatisticsViewModel()
    {
        _sut.StatisticsViewCommand.Execute(null);

        Assert.IsType<StatisticsViewModel>(_sut.CurrentView);
    }

    [Fact]
    public void DatabaseViewCommand_SetsDatabaseViewModel()
    {
        _sut.DatabaseViewCommand.Execute(null);

        Assert.IsType<DatabaseViewModel>(_sut.CurrentView);
    }

    [Fact]
    public void DatabaseVM_IsSameInstanceOnRepeatedNavigation()
    {
        _sut.DatabaseViewCommand.Execute(null);
        var first = _sut.CurrentView;
        _sut.HomeViewCommand.Execute(null);
        _sut.DatabaseViewCommand.Execute(null);

        Assert.Same(first, _sut.CurrentView);
    }
}
