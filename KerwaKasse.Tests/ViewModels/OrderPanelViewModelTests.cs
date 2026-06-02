using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;
using KerwaKasse.MVVM.ViewModel;
using NSubstitute;

namespace KerwaKasse.Tests.ViewModels;

public class OrderPanelViewModelTests
{
    private readonly IProductService _productService;
    private readonly IOrderService _orderService;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialogService;
    private readonly OrderPanelViewModel _sut;

    public OrderPanelViewModelTests()
    {
        _productService = Substitute.For<IProductService>();
        _orderService = Substitute.For<IOrderService>();
        _settings = Substitute.For<ISettingsService>();
        _dialogService = Substitute.For<IDialogService>();

        _productService.GetAvailable().Returns(new List<Product>
        {
            new() { Id = 1, Description = "Bratwurst", Price = 3.50m, Available = true, SortOrder = 1 },
            new() { Id = 2, Description = "Bier", Price = 2.80m, Available = true, SortOrder = 2 }
        });

        _settings.Get("orderPanelCardScaleFactor", 1.0).Returns(1.0);
        _settings.Get("orderPanelUseColoredBorder", true).Returns(true);
        _settings.Get("orderPanelBorderDarkenFactor", 0.7).Returns(0.7);
        _settings.Get("orderPanelWidth", 350.0).Returns(350.0);

        _sut = new OrderPanelViewModel(_productService, _orderService, _settings, _dialogService);
    }

    [Fact]
    public void Constructor_LoadsAvailableProducts()
    {
        Assert.Equal(2, _sut.Products.Count);
        Assert.Equal("Bratwurst", _sut.Products[0].Description);
    }

    [Fact]
    public void AddOrderPosition_AddsNewProduct()
    {
        var product = _sut.Products[0];

        _sut.AddOrderPosition(product);

        Assert.Single(_sut.OrderPositions);
        Assert.Equal(1, _sut.OrderPositions[0].Amount);
        Assert.Equal(3.50m, _sut.Total);
    }

    [Fact]
    public void AddOrderPosition_SameProductTwice_IncrementsAmount()
    {
        var product = _sut.Products[0];

        _sut.AddOrderPosition(product);
        _sut.AddOrderPosition(product);

        Assert.Single(_sut.OrderPositions);
        Assert.Equal(2, _sut.OrderPositions[0].Amount);
        Assert.Equal(7.00m, _sut.Total);
    }

    [Fact]
    public void AddOrderPosition_DifferentProducts_AddsBoth()
    {
        _sut.AddOrderPosition(_sut.Products[0]); // Bratwurst 3.50
        _sut.AddOrderPosition(_sut.Products[1]); // Bier 2.80

        Assert.Equal(2, _sut.OrderPositions.Count);
        Assert.Equal(6.30m, _sut.Total);
    }

    [Fact]
    public void DiscardOrder_ClearsPositionsAndTotal()
    {
        _sut.AddOrderPosition(_sut.Products[0]);
        _sut.AddOrderPosition(_sut.Products[1]);

        _sut.DiscardOrder();

        Assert.Empty(_sut.OrderPositions);
        Assert.Equal(0m, _sut.Total);
    }

    [Fact]
    public void SaveOrder_CallsPlaceOrder_And_Discards()
    {
        _sut.AddOrderPosition(_sut.Products[0]);

        _sut.SaveOrder();

        _orderService.Received(1).PlaceOrder(Arg.Any<IEnumerable<OrderPosition>>());
        Assert.Empty(_sut.OrderPositions);
        Assert.Equal(0m, _sut.Total);
    }

    [Fact]
    public void SaveOrder_MapsPositionsCorrectly()
    {
        _sut.AddOrderPosition(_sut.Products[0]);
        _sut.AddOrderPosition(_sut.Products[0]); // Amount = 2

        List<OrderPosition>? captured = null;
        _orderService.When(x => x.PlaceOrder(Arg.Any<IEnumerable<OrderPosition>>()))
            .Do(ci => captured = ci.Arg<IEnumerable<OrderPosition>>().ToList());

        _sut.SaveOrder();

        Assert.NotNull(captured);
        var positions = captured!.ToList();
        Assert.Single(positions);
        Assert.Equal(1, positions[0].ProductId);
        Assert.Equal(2, positions[0].Amount);
        Assert.Equal(3.50m, positions[0].UnitPrice);
    }

    [Fact]
    public void SaveOrder_OnException_ShowsError()
    {
        _sut.AddOrderPosition(_sut.Products[0]);
        _orderService.When(x => x.PlaceOrder(Arg.Any<IEnumerable<OrderPosition>>()))
            .Do(_ => throw new InvalidOperationException("DB error"));

        _sut.SaveOrder();

        _dialogService.Received(1).ShowError("DB error");
    }

    [Fact]
    public void Constructor_LoadsPreferencesFromSettings()
    {
        Assert.Equal(1.0, _sut.Button_ResizeFactor);
        Assert.Equal(350.0, _sut.OrderPanelWidth);
        Assert.True(_sut.UseColoredBorder);
        Assert.Equal(0.7, _sut.BorderDarkenFactor);
    }

    [Fact]
    public void SaveOrder_WithEmptyPositions_DoesNotCallPlaceOrder()
    {
        // No positions added
        _sut.SaveOrder();

        _orderService.DidNotReceive().PlaceOrder(Arg.Any<IEnumerable<OrderPosition>>());
    }
}
