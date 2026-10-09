using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;
using KerwaKasse.MVVM.ViewModel;
using Microsoft.Extensions.Logging.Abstractions;
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
            new() { Id = 1, Name = "Bratwurst", Price = 3.50m, Available = true, SortOrder = 1 },
            new() { Id = 2, Name = "Bier", Price = 2.80m, Available = true, SortOrder = 2 }
        });

        _settings.Get("orderPanelCardScaleFactor", 1.0).Returns(1.0);
        _settings.Get("orderPanelUseColoredBorder", true).Returns(true);
        _settings.Get("orderPanelBorderDarkenFactor", 0.7).Returns(0.7);
        _settings.Get("orderPanelWidth", 350.0).Returns(350.0);
        _settings.Get("orderPanelAutoFitTiles", true).Returns(false); // tests start in manual mode
        _settings.Get("orderPanelAutoFitMaxScale", 2.5).Returns(2.5);

        _sut = new OrderPanelViewModel(_productService, _orderService, _settings, _dialogService, NullLogger<OrderPanelViewModel>.Instance);
    }

    [Fact]
    public void Constructor_LoadsAvailableProducts()
    {
        Assert.Equal(2, _sut.Products.Count);
        Assert.Equal("Bratwurst", _sut.Products[0].Name);
    }

    [Fact]
    public void Arranging_StartsAndFinishes()
    {
        Assert.False(_sut.IsArrangingTiles);

        _sut.StartArrangingCommand.Execute(null);
        Assert.True(_sut.IsArrangingTiles);

        _sut.FinishArrangingCommand.Execute(null);
        Assert.False(_sut.IsArrangingTiles);
    }

    [Fact]
    public void MoveProduct_WhileArranging_MovesTileAndSavesOrder()
    {
        var bier = _sut.Products[1];
        var move = new DragReorderMove(bier, 1, 0);
        _sut.StartArrangingCommand.Execute(null);

        Assert.True(_sut.MoveProductCommand.CanExecute(move));
        _sut.MoveProductCommand.Execute(move);

        Assert.Same(bier, _sut.Products[0]);
        _productService.Received(1).MoveAvailable(2, 0);
    }

    [Fact]
    public void MoveProduct_OnlyWhileArranging()
    {
        var move = new DragReorderMove(_sut.Products[1], 1, 0);

        Assert.False(_sut.MoveProductCommand.CanExecute(move));
    }

    [Fact]
    public void ComputeAutoFitFactor_PicksLargestFactorWhereAllTilesFit()
    {
        // 4 tiles in an area that holds exactly 2×2 tiles at factor 1.0 (footprint 178×113 each).
        Assert.Equal(1.0, OrderPanelViewModel.ComputeAutoFitFactor(356, 226, 4, 2.5), 2);

        // Halving the height forces a smaller factor.
        Assert.True(OrderPanelViewModel.ComputeAutoFitFactor(356, 113, 4, 2.5) < 1.0);

        // The configurable maximum caps the factor even in a huge area.
        Assert.Equal(2.5, OrderPanelViewModel.ComputeAutoFitFactor(10000, 10000, 1, 2.5), 2);
        Assert.Equal(1.2, OrderPanelViewModel.ComputeAutoFitFactor(10000, 10000, 1, 1.2), 2);
    }

    [Fact]
    public void AutoFitTiles_ComputesEffectiveScale_WithoutTouchingManualSlider()
    {
        _sut.Button_ResizeFactor = 1.4; // the user's own manual choice
        _sut.UpdateAutoFitArea(178, 226); // exactly one column, two rows at factor 1.0

        _sut.AutoFitTiles = true;

        Assert.Equal(1.0, _sut.EffectiveScaleFactor, 2); // computed from the area
        Assert.Equal(1.4, _sut.Button_ResizeFactor, 2);  // manual slider's own value, unaffected
        Assert.False(_sut.IsManualTileSize);
        _settings.Received().Set("orderPanelAutoFitTiles", true);
    }

    [Fact]
    public void AutoFitMaxFactor_NeverChangesTheManualSliderValue()
    {
        // Regression: dragging the "maximale Größe" slider used to bleed into the (disabled)
        // manual-size slider because both were the same bound property.
        _sut.Button_ResizeFactor = 1.4;
        _sut.UpdateAutoFitArea(10000, 10000); // huge area, so the max factor is always the limiting bound
        _sut.AutoFitTiles = true;

        _sut.AutoFitMaxFactor = 1.2;

        Assert.Equal(1.2, _sut.EffectiveScaleFactor, 2);
        Assert.Equal(1.4, _sut.Button_ResizeFactor, 2);
    }

    [Fact]
    public void DisablingAutoFit_HandsControlBackToTheManualSliderValue()
    {
        _sut.Button_ResizeFactor = 1.4;
        _sut.UpdateAutoFitArea(178, 226);
        _sut.AutoFitTiles = true;
        Assert.Equal(1.0, _sut.EffectiveScaleFactor, 2);

        _sut.AutoFitTiles = false;

        Assert.Equal(1.4, _sut.EffectiveScaleFactor, 2);
        Assert.True(_sut.IsManualTileSize);
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
    public void DecreaseOrderPosition_TakesOneOff()
    {
        var product = _sut.Products[0]; // Bratwurst 3.50
        _sut.AddOrderPosition(product);
        _sut.AddOrderPosition(product);
        _sut.AddOrderPosition(product);

        _sut.DecreaseOrderPosition(_sut.OrderPositions[0]);

        Assert.Single(_sut.OrderPositions);
        Assert.Equal(2, _sut.OrderPositions[0].Amount);
        Assert.Equal(7.00m, _sut.Total);
    }

    [Fact]
    public void DecreaseOrderPosition_AtAmountOne_RemovesOnlyThatPosition()
    {
        _sut.AddOrderPosition(_sut.Products[0]); // Bratwurst 3.50
        _sut.AddOrderPosition(_sut.Products[1]); // Bier 2.80

        _sut.DecreaseOrderPosition(_sut.OrderPositions[0]);

        Assert.Single(_sut.OrderPositions);
        Assert.Equal("Bier", _sut.OrderPositions[0].Product.Name);
        Assert.Equal(2.80m, _sut.Total);
    }

    [Fact]
    public void DecreaseOrderPositionCommand_OnLastPosition_EmptiesTheOrder()
    {
        _sut.AddOrderPosition(_sut.Products[1]);

        _sut.DecreaseOrderPositionCommand.Execute(_sut.OrderPositions[0]);

        Assert.Empty(_sut.OrderPositions);
        Assert.Equal(0m, _sut.Total);
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

    // ── Variable prices and free entries ──

    private ProductModel VariablePriceProduct()
    {
        var mackerel = new ProductModel { ProductID = 9, Name = "Makrele", Price = 7.00m, VariablePrice = true, ColorAsString = "#00AA00" };
        _sut.Products.Add(mackerel);
        return mackerel;
    }

    [Fact]
    public async Task AddOrderPosition_VariablePrice_AsksForThePriceStartingFromTheSuggestion()
    {
        var mackerel = VariablePriceProduct();
        _dialogService.ShowPriceEntryAsync("Makrele", 7.00m).Returns(8.50m);

        await _sut.AddOrderPosition(mackerel);

        var position = Assert.Single(_sut.OrderPositions);
        Assert.Equal(OrderPositionKind.VariablePrice, position.Kind);
        Assert.Equal(8.50m, position.UnitPrice);
        Assert.Equal(7.00m, position.ListPrice);
        Assert.Equal(8.50m, _sut.Total);
        Assert.Equal("Makrele", position.DisplayName);
        Assert.Equal("Variabler Preis", position.Subline);
    }

    [Fact]
    public async Task AddOrderPosition_VariablePrice_CancelledAddsNothing()
    {
        var mackerel = VariablePriceProduct();
        _dialogService.ShowPriceEntryAsync(Arg.Any<string>(), Arg.Any<decimal>()).Returns((decimal?)null);

        await _sut.AddOrderPosition(mackerel);

        Assert.Empty(_sut.OrderPositions);
        Assert.Equal(0m, _sut.Total);
    }

    [Fact]
    public async Task AddOrderPosition_VariablePrice_SamePriceSharesARow_OtherPriceGetsItsOwn()
    {
        var mackerel = VariablePriceProduct();
        _dialogService.ShowPriceEntryAsync(Arg.Any<string>(), Arg.Any<decimal>()).Returns(8.50m, 8.50m, 9.00m);

        await _sut.AddOrderPosition(mackerel);
        await _sut.AddOrderPosition(mackerel);
        await _sut.AddOrderPosition(mackerel);

        Assert.Equal(2, _sut.OrderPositions.Count);
        Assert.Equal(2, _sut.OrderPositions.Single(p => p.UnitPrice == 8.50m).Amount);
        Assert.Equal(26.00m, _sut.Total);
    }

    [Fact]
    public async Task AddFreeEntry_OffersRecentNotesAndTheAvailableProducts()
    {
        var recent = new List<string> { "Kuchen" };
        _orderService.GetRecentNotes(5).Returns(recent);

        await _sut.AddFreeEntry();

        await _dialogService.Received(1).ShowFreeEntryAsync(recent, _sut.Products);
    }

    [Fact]
    public async Task AddFreeEntry_WithProduct_IsASpecialPriceForIt()
    {
        var bratwurst = _sut.Products[0];
        _dialogService.ShowFreeEntryAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<ProductModel>>())
            .Returns(new FreeEntryResult(5.00m, "3 statt 2", bratwurst));

        await _sut.AddFreeEntry();

        var position = Assert.Single(_sut.OrderPositions);
        Assert.Equal(OrderPositionKind.FreeEntry, position.Kind);
        Assert.Equal(1, position.ProductId);
        Assert.Equal(5.00m, position.UnitPrice);
        Assert.Equal(3.50m, position.ListPrice);
        // The main line says what was sold, the second line only the kind.
        Assert.Equal("Bratwurst · 3 statt 2", position.DisplayName);
        Assert.Equal("Sonderpreis", position.Subline);
    }

    [Fact]
    public async Task AddFreeEntry_WithoutProduct_ShowsItsDescriptionAsName()
    {
        _dialogService.ShowFreeEntryAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<ProductModel>>())
            .Returns(new FreeEntryResult(2.50m, "Kuchen", null));

        await _sut.AddFreeEntry();

        var position = Assert.Single(_sut.OrderPositions);
        Assert.Null(position.ProductId);
        Assert.Null(position.ListPrice);
        Assert.Equal("Kuchen", position.DisplayName);
        Assert.Equal("Freie Eingabe", position.Subline);
        Assert.False(position.IsWithoutDescription);
    }

    [Fact]
    public async Task AddFreeEntry_WithoutDescription_IsMarked()
    {
        _dialogService.ShowFreeEntryAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<ProductModel>>())
            .Returns(new FreeEntryResult(2.50m, null, null));

        await _sut.AddFreeEntry();

        var position = Assert.Single(_sut.OrderPositions);
        Assert.Equal("Ohne Angabe", position.DisplayName);
        Assert.Equal("Freie Eingabe", position.Subline);
        Assert.True(position.IsWithoutDescription);
    }

    [Fact]
    public async Task AddFreeEntry_Cancelled_AddsNothing()
    {
        _dialogService.ShowFreeEntryAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<ProductModel>>())
            .Returns((FreeEntryResult?)null);

        await _sut.AddFreeEntry();

        Assert.Empty(_sut.OrderPositions);
    }

    [Fact]
    public async Task RegularTap_DoesNotMergeIntoASpecialPriceOfTheSameProduct()
    {
        var bratwurst = _sut.Products[0];
        _dialogService.ShowFreeEntryAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<ProductModel>>())
            .Returns(new FreeEntryResult(5.00m, null, bratwurst));
        await _sut.AddFreeEntry();

        await _sut.AddOrderPosition(bratwurst);

        Assert.Equal(2, _sut.OrderPositions.Count);
        Assert.Equal(8.50m, _sut.Total);
    }

    [Fact]
    public async Task SaveOrder_StoresFreeEntriesWithKindNoteAndListPrice()
    {
        _dialogService.ShowFreeEntryAsync(Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<ProductModel>>())
            .Returns(new FreeEntryResult(2.50m, "Kuchen", null));
        await _sut.AddFreeEntry();
        List<OrderPosition>? captured = null;
        _orderService.When(x => x.PlaceOrder(Arg.Any<IEnumerable<OrderPosition>>()))
            .Do(ci => captured = ci.Arg<IEnumerable<OrderPosition>>().ToList());

        _sut.SaveOrder();

        var position = Assert.Single(captured!);
        Assert.Equal(OrderPositionKind.FreeEntry, position.Kind);
        Assert.Null(position.ProductId);
        Assert.Equal(2.50m, position.UnitPrice);
        Assert.Null(position.ListPrice);
        Assert.Equal("Kuchen", position.Note);
    }

    [Fact]
    public void SaveOrder_StoresTheListPriceAsRegularPosition()
    {
        var product = _sut.Products[0];
        _sut.AddOrderPosition(product);
        List<OrderPosition>? captured = null;
        _orderService.When(x => x.PlaceOrder(Arg.Any<IEnumerable<OrderPosition>>()))
            .Do(ci => captured = ci.Arg<IEnumerable<OrderPosition>>().ToList());

        _sut.SaveOrder();

        var position = Assert.Single(captured!);
        Assert.Equal(OrderPositionKind.Regular, position.Kind);
        Assert.Equal(product.Price, position.UnitPrice);
        Assert.Equal(product.Price, position.ListPrice);
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
