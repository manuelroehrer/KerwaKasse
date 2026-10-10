using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.Helper;
using KerwaKasse.MVVM.Model;
using KerwaKasse.MVVM.ViewModel;
using NSubstitute;

namespace KerwaKasse.Tests.ViewModels;

public class OrderHistoryViewModelTests
{
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly IDialogService _dialogService;

    public OrderHistoryViewModelTests()
    {
        _orderService = Substitute.For<IOrderService>();
        _productService = Substitute.For<IProductService>();
        _dialogService = Substitute.For<IDialogService>();

        _orderService.GetOrdersByDate(Arg.Any<DateTime>()).Returns(new List<Order>());
        _orderService.GetRecentNotes(Arg.Any<int>()).Returns(new List<string>());
        _productService.GetAll().Returns(new List<Product>
        {
            new() { Id = 1, Name = "Bratwurst", Price = 3.50m },
            new() { Id = 2, Name = "Bier", Price = 2.80m },
            new() { Id = 3, Name = "Brezel", Price = 1.20m },
            new() { Id = 4, Name = "Makrele", Price = 7.00m, VariablePrice = true }
        });
    }

    private OrderHistoryViewModel CreateSut() => new(_orderService, _productService, _dialogService);

    // An order with a mackerel at an entered price, a special price for Bratwurst and a described free entry.
    private void SetUpOrderWithEnteredPrices()
    {
        _orderService.GetOrdersByDate(DateTime.Today).Returns(new List<Order>
        {
            new()
            {
                Id = 5,
                OrderTime = DateTime.Today.AddHours(18),
                Positions =
                [
                    new OrderPosition { ProductId = 4, ProductName = "Makrele", Kind = OrderPositionKind.VariablePrice, Amount = 2, UnitPrice = 8.50m, ListPrice = 7.00m },
                    new OrderPosition { ProductId = 1, ProductName = "Bratwurst", Kind = OrderPositionKind.FreeEntry, Amount = 1, UnitPrice = 2.00m, ListPrice = 3.50m, Note = "Helfer" },
                    new OrderPosition { ProductName = "", Kind = OrderPositionKind.FreeEntry, Amount = 1, UnitPrice = 4.00m, Note = "Kuchenspende" }
                ]
            }
        });
    }

    // "Position nachtragen" picks the product with the given id, or "Freie Eingabe" for null; returns
    // what the dialog offered.
    private Func<IReadOnlyList<ProductModel>> PickOnAdd(int? productId)
    {
        IReadOnlyList<ProductModel> offered = [];
        _dialogService.ShowAddPositionAsync(Arg.Do<IReadOnlyList<ProductModel>>(p => offered = p))
            .Returns(call => new AddPositionChoice(productId is int id
                ? call.Arg<IReadOnlyList<ProductModel>>().Single(p => p.ProductID == id)
                : null));
        return () => offered;
    }

    private OrderHistoryViewModel CreateSutEditing()
    {
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);
        return sut;
    }

    private void SetUpSingleOrder(int id = 1)
    {
        _orderService.GetOrdersByDate(DateTime.Today).Returns(new List<Order>
        {
            new()
            {
                Id = id,
                OrderTime = DateTime.Today.AddHours(12),
                Positions =
                [
                    new OrderPosition { ProductId = 1, ProductName = "Bratwurst", Amount = 2, UnitPrice = 3.50m }
                ]
            }
        });
    }

    [Fact]
    public void Constructor_SetsDateToTodayAndLoads()
    {
        var sut = CreateSut();
        Assert.Equal(DateTime.Today, sut.Date);
        _orderService.Received().GetOrdersByDate(DateTime.Today);
    }

    [Fact]
    public void SelectingSale_DoesNotEnterEditMode()
    {
        SetUpSingleOrder();
        var sut = CreateSut();

        sut.SelectedOrder = sut.Orders[0];

        Assert.True(sut.HasSelection);
        Assert.False(sut.IsEditing);
        Assert.Empty(sut.EditPositions);
    }

    [Fact]
    public void BeginEdit_PopulatesEditorFromSale()
    {
        SetUpSingleOrder();
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];

        sut.BeginEditCommand.Execute(null);

        Assert.True(sut.IsEditing);
        Assert.Single(sut.EditPositions);
        Assert.Equal(2, sut.EditPositions[0].Amount);
        Assert.Equal(7.00m, sut.EditTotal);
    }

    [Fact]
    public void ChangingAmount_FlagsPositionAsChanged()
    {
        SetUpSingleOrder();
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);

        sut.EditPositions[0].Amount = 5;

        Assert.True(sut.EditPositions[0].IsModified);
        Assert.True(sut.EditPositions[0].IsChanged);
    }

    [Fact]
    public void AddPosition_ViaPicker_AddsNewHighlightedPosition()
    {
        SetUpSingleOrder();
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);

        var offered = PickOnAdd(2); // Bier
        sut.AddPositionCommand.Execute(null);

        // The dialog offers everything except the already-present Bratwurst (1).
        Assert.DoesNotContain(offered(), p => p.ProductID == 1);

        Assert.Equal(2, sut.EditPositions.Count);
        var added = sut.EditPositions.Single(p => p.ProductId == 2);
        Assert.True(added.IsNew);
        Assert.Equal(1, added.Amount);
        Assert.Equal(9.80m, sut.EditTotal); // 7.00 + 2.80
    }

    [Fact]
    public void ToggleRemove_OnExistingPosition_GreysOutAndExcludesFromTotal()
    {
        SetUpSingleOrder();
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);

        sut.ToggleRemovePositionCommand.Execute(sut.EditPositions[0]);

        // Still present (greyed out, not gone), but excluded from total and blocks save.
        Assert.Single(sut.EditPositions);
        Assert.True(sut.EditPositions[0].IsRemoved);
        Assert.Equal(0m, sut.EditTotal);
        Assert.False(sut.CanSave);
    }

    [Fact]
    public void Amount_IsClampedToValidRange()
    {
        SetUpSingleOrder();
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);
        var pos = sut.EditPositions[0];

        pos.Amount = 0;
        Assert.Equal(1, pos.Amount);
        pos.Amount = 250;
        Assert.Equal(100, pos.Amount);
    }

    [Fact]
    public void BeginEdit_WithoutChanges_CannotSave()
    {
        SetUpSingleOrder();
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);

        // Nothing changed yet -> saving is not offered.
        Assert.True(sut.HasActivePositions);
        Assert.False(sut.HasChanges);
        Assert.False(sut.CanSave);
    }

    [Fact]
    public void ChangingAmount_EnablesSave()
    {
        SetUpSingleOrder();
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);

        sut.EditPositions[0].Amount = 5;

        Assert.True(sut.HasChanges);
        Assert.True(sut.CanSave);
    }

    [Fact]
    public void InputError_BlocksSave()
    {
        SetUpSingleOrder();
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);
        sut.EditPositions[0].Amount = 5; // a real change -> would otherwise allow saving

        sut.HasInputError = true;
        Assert.False(sut.CanSave);

        sut.HasInputError = false;
        Assert.True(sut.CanSave);
    }

    [Fact]
    public void ToggleRemove_OnNewPosition_DropsItEntirely()
    {
        SetUpSingleOrder();
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);
        PickOnAdd(2);
        sut.AddPositionCommand.Execute(null);

        var added = sut.EditPositions.Single(p => p.ProductId == 2);
        sut.ToggleRemovePositionCommand.Execute(added);

        Assert.DoesNotContain(sut.EditPositions, p => p.ProductId == 2);
    }

    [Fact]
    public void Save_PersistsActivePositionsAndExitsEdit()
    {
        SetUpSingleOrder();
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);
        PickOnAdd(2);
        sut.AddPositionCommand.Execute(null);

        sut.SaveCommand.Execute(null);

        _orderService.Received(1).ReplaceOrderPositions(Arg.Is<Order>(o =>
            o.Id == 1 &&
            o.Positions.Count == 2 &&
            o.Positions.Any(p => p.ProductId == 1 && p.UnitPrice == 3.50m) &&
            o.Positions.Any(p => p.ProductId == 2 && p.UnitPrice == 2.80m && p.ListPrice == 2.80m)));
        Assert.False(sut.IsEditing);
    }

    [Fact]
    public void Save_KeepsKindListPriceAndNoteOfUntouchedPositions()
    {
        _orderService.GetOrdersByDate(DateTime.Today).Returns(new List<Order>
        {
            new()
            {
                Id = 3,
                OrderTime = DateTime.Today,
                Positions =
                [
                    new OrderPosition { ProductId = 1, ProductName = "Bratwurst", Amount = 1, UnitPrice = 3.50m, ListPrice = 3.50m },
                    new OrderPosition { Kind = OrderPositionKind.FreeEntry, Amount = 1, UnitPrice = 2.50m, Note = "Kuchen" }
                ]
            }
        });
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);

        sut.EditPositions.Single(p => p.ProductId == 1).Amount = 2;
        sut.SaveCommand.Execute(null);

        _orderService.Received(1).ReplaceOrderPositions(Arg.Is<Order>(o =>
            o.Positions.Count == 2 &&
            o.Positions.Any(p => p.ProductId == 1 && p.Amount == 2 && p.ListPrice == 3.50m && p.Kind == OrderPositionKind.Regular) &&
            o.Positions.Any(p => p.ProductId == null && p.Kind == OrderPositionKind.FreeEntry && p.Note == "Kuchen" && p.UnitPrice == 2.50m)));
    }

    [Fact]
    public void Save_ExcludesRemovedPositions()
    {
        // Two positions, remove one, the saved order keeps only the active one.
        _orderService.GetOrdersByDate(DateTime.Today).Returns(new List<Order>
        {
            new()
            {
                Id = 7,
                OrderTime = DateTime.Today,
                Positions =
                [
                    new OrderPosition { ProductId = 1, ProductName = "Bratwurst", Amount = 1, UnitPrice = 3.50m },
                    new OrderPosition { ProductId = 2, ProductName = "Bier", Amount = 2, UnitPrice = 2.80m }
                ]
            }
        });
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);

        sut.ToggleRemovePositionCommand.Execute(sut.EditPositions.Single(p => p.ProductId == 2));
        sut.SaveCommand.Execute(null);

        _orderService.Received(1).ReplaceOrderPositions(Arg.Is<Order>(o =>
            o.Positions.Count == 1 && o.Positions[0].ProductId == 1));
    }

    [Fact]
    public void Save_WhenAllRemoved_DoesNotPersist()
    {
        SetUpSingleOrder();
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);
        sut.ToggleRemovePositionCommand.Execute(sut.EditPositions[0]);

        sut.SaveCommand.Execute(null);

        _orderService.DidNotReceive().ReplaceOrderPositions(Arg.Any<Order>());
    }

    [Fact]
    public void Cancel_DiscardsEditsAndExitsEdit()
    {
        SetUpSingleOrder();
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);
        sut.EditPositions[0].Amount = 99;

        sut.CancelEditCommand.Execute(null);

        Assert.False(sut.IsEditing);
        Assert.Empty(sut.EditPositions);
        _orderService.DidNotReceive().ReplaceOrderPositions(Arg.Any<Order>());
    }

    [Fact]
    public void Delete_CallsDeleteOrderAndClearsSelection()
    {
        SetUpSingleOrder();
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];

        sut.DeleteOrderCommand.Execute(null);

        _orderService.Received(1).DeleteOrder(1);
        Assert.False(sut.HasSelection);
    }

    [Fact]
    public void Orders_ShowEnteredPricesAndFreeEntries()
    {
        SetUpOrderWithEnteredPrices();
        var sut = CreateSut();

        var order = sut.Orders[0];
        // The charged prices count, not the normal ones.
        Assert.Equal(23.00m, order.Total); // 2 x 8.50 + 2.00 + 4.00
        Assert.Equal("Makrele, Bratwurst · Helfer, Kuchenspende", order.ShortDescription);

        var free = order.OrderPositions[2];
        Assert.Null(free.Product);
        Assert.Equal("Kuchenspende", free.DisplayName);
        Assert.Equal("Freie Eingabe", free.Subline);
        Assert.Equal("Sonderpreis", order.OrderPositions[1].Subline);
        Assert.Equal("Variabler Preis", order.OrderPositions[0].Subline);
        Assert.Equal(7.00m, order.OrderPositions[0].Product.Price);
        Assert.Equal(8.50m, order.OrderPositions[0].UnitPrice);
    }

    [Theory]
    [InlineData("kuchen")]
    [InlineData("Helfer")]
    [InlineData("Freie Eingabe")]
    [InlineData("Sonderpreis")]
    [InlineData("variabler preis")]
    public void Search_FindsDescriptionsOfFreeEntries(string search)
    {
        SetUpOrderWithEnteredPrices();
        var sut = CreateSut();

        sut.SearchText = search;

        Assert.Single(sut.Orders);
    }

    [Fact]
    public void Search_WithoutMatch_HidesOrder()
    {
        SetUpOrderWithEnteredPrices();
        var sut = CreateSut();

        sut.SearchText = "Spätzle";

        Assert.Empty(sut.Orders);
    }

    [Fact]
    public void BeginEdit_OnlyEnteredPricesAndFreeEntriesCanBeChanged()
    {
        _orderService.GetOrdersByDate(DateTime.Today).Returns(new List<Order>
        {
            new()
            {
                Id = 5,
                OrderTime = DateTime.Today,
                Positions =
                [
                    new OrderPosition { ProductId = 2, ProductName = "Bier", Amount = 1, UnitPrice = 2.80m, ListPrice = 2.80m },
                    new OrderPosition { ProductId = 4, ProductName = "Makrele", Kind = OrderPositionKind.VariablePrice, Amount = 1, UnitPrice = 8.50m, ListPrice = 7.00m },
                    new OrderPosition { ProductName = "", Kind = OrderPositionKind.FreeEntry, Amount = 1, UnitPrice = 4.00m, Note = "Kuchenspende" }
                ]
            }
        });
        var sut = CreateSutEditing();

        var (regular, variable, free) = (sut.EditPositions[0], sut.EditPositions[1], sut.EditPositions[2]);
        Assert.False(regular.CanEditPrice);
        Assert.False(regular.CanEditPurpose);
        Assert.True(variable.CanEditPrice);
        Assert.False(variable.CanEditPurpose);
        Assert.True(free.CanEditPrice);
        Assert.True(free.CanEditPurpose);

        sut.ToggleRemovePositionCommand.Execute(free);
        Assert.False(free.CanEditPrice);
        Assert.False(free.CanEditPurpose);
    }

    [Fact]
    public void EditPrice_SetsTheEnteredPriceAndFlagsTheChange()
    {
        SetUpOrderWithEnteredPrices();
        _dialogService.ShowPriceEntryAsync("Makrele", 8.50m, true).Returns(9.00m);
        var sut = CreateSutEditing();
        var mackerel = sut.EditPositions[0];

        sut.EditPriceCommand.Execute(mackerel);

        Assert.Equal(9.00m, mackerel.UnitPrice);
        Assert.True(mackerel.IsModified);
        Assert.Equal(24.00m, sut.EditTotal); // 2 x 9.00 + 2.00 + 4.00
        Assert.True(sut.CanSave);
    }

    [Fact]
    public void EditPrice_Cancelled_KeepsThePrice()
    {
        SetUpOrderWithEnteredPrices();
        _dialogService.ShowPriceEntryAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<bool>()).Returns((decimal?)null);
        var sut = CreateSutEditing();

        sut.EditPriceCommand.Execute(sut.EditPositions[0]);

        Assert.Equal(8.50m, sut.EditPositions[0].UnitPrice);
        Assert.False(sut.HasChanges);
    }

    [Fact]
    public void EditPrice_OnRegularPosition_DoesNotAsk()
    {
        SetUpSingleOrder();
        var sut = CreateSutEditing();

        sut.EditPriceCommand.Execute(sut.EditPositions[0]);

        _dialogService.DidNotReceive().ShowPriceEntryAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<bool>());
    }

    [Fact]
    public void EditPrice_ChangedBackToTheOriginal_IsNoChange()
    {
        SetUpOrderWithEnteredPrices();
        _dialogService.ShowPriceEntryAsync(Arg.Any<string>(), Arg.Any<decimal>(), true).Returns(9.00m, 8.50m);
        var sut = CreateSutEditing();

        sut.EditPriceCommand.Execute(sut.EditPositions[0]);
        sut.EditPriceCommand.Execute(sut.EditPositions[0]);

        Assert.False(sut.EditPositions[0].IsModified);
        Assert.False(sut.HasChanges);
    }

    [Fact]
    public void EditPurpose_ChangesDescriptionAndProduct()
    {
        SetUpOrderWithEnteredPrices();
        FreeEntryResult? shown = null;
        _dialogService.ShowFreeEntryPurposeAsync(Arg.Do<FreeEntryResult>(r => shown = r), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<ProductModel>>())
            .Returns(call => new FreeEntryResult(4.00m, "Vom Vortag", call.ArgAt<IReadOnlyList<ProductModel>>(2).Single(p => p.ProductID == 3)));
        var sut = CreateSutEditing();
        var free = sut.EditPositions[2];

        sut.EditPurposeCommand.Execute(free);

        // The dialog starts from the entry as it is.
        Assert.Equal(new FreeEntryResult(4.00m, "Kuchenspende", null), shown);
        Assert.Equal(3, free.ProductId);
        Assert.Equal("Brezel · Vom Vortag", free.DisplayName);
        Assert.Equal("Sonderpreis", free.Subline);
        Assert.Equal(1.20m, free.ListPrice);
        Assert.True(free.IsModified);
        Assert.True(sut.CanSave);
    }

    [Fact]
    public void EditPurpose_OfASpecialPrice_StartsWithItsProduct()
    {
        SetUpOrderWithEnteredPrices();
        FreeEntryResult? shown = null;
        _dialogService.ShowFreeEntryPurposeAsync(Arg.Do<FreeEntryResult>(r => shown = r), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<ProductModel>>())
            .Returns(call => new FreeEntryResult(2.00m, null, null));
        var sut = CreateSutEditing();
        var special = sut.EditPositions[1];

        sut.EditPurposeCommand.Execute(special);

        Assert.Equal(1, shown!.Product.ProductID);
        Assert.Equal("Helfer", shown!.Note);
        // Now without anything: "Ohne Angabe", no product and no normal price to compare with.
        Assert.Null(special.ProductId);
        Assert.Null(special.ListPrice);
        Assert.True(special.IsWithoutDescription);
        Assert.Equal("Ohne Angabe", special.DisplayName);
        Assert.Equal("Freie Eingabe", special.Subline);
    }

    [Fact]
    public void EditPurpose_Cancelled_ChangesNothing()
    {
        SetUpOrderWithEnteredPrices();
        _dialogService.ShowFreeEntryPurposeAsync(Arg.Any<FreeEntryResult>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<ProductModel>>())
            .Returns((FreeEntryResult?)null);
        var sut = CreateSutEditing();

        sut.EditPurposeCommand.Execute(sut.EditPositions[2]);

        Assert.Equal("Kuchenspende", sut.EditPositions[2].Note);
        Assert.False(sut.HasChanges);
    }

    [Fact]
    public void Save_PersistsChangedPriceAndPurpose()
    {
        SetUpOrderWithEnteredPrices();
        _dialogService.ShowPriceEntryAsync(Arg.Any<string>(), Arg.Any<decimal>(), true).Returns(9.00m);
        _dialogService.ShowFreeEntryPurposeAsync(Arg.Any<FreeEntryResult>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<IReadOnlyList<ProductModel>>())
            .Returns(new FreeEntryResult(4.00m, "Torte", null));
        var sut = CreateSutEditing();

        sut.EditPriceCommand.Execute(sut.EditPositions[0]);
        sut.EditPurposeCommand.Execute(sut.EditPositions[2]);
        sut.SaveCommand.Execute(null);

        _orderService.Received(1).ReplaceOrderPositions(Arg.Is<Order>(o =>
            o.Positions.Count == 3 &&
            o.Positions.Any(p => p.ProductId == 4 && p.Kind == OrderPositionKind.VariablePrice && p.UnitPrice == 9.00m && p.ListPrice == 7.00m && p.Amount == 2) &&
            o.Positions.Any(p => p.ProductId == 1 && p.Kind == OrderPositionKind.FreeEntry && p.UnitPrice == 2.00m && p.Note == "Helfer") &&
            o.Positions.Any(p => p.ProductId == null && p.Kind == OrderPositionKind.FreeEntry && p.UnitPrice == 4.00m && p.Note == "Torte")));
    }

    [Fact]
    public void AddPosition_VariablePrice_AsksForThePriceAndCanAppearAgain()
    {
        SetUpOrderWithEnteredPrices();
        _dialogService.ShowPriceEntryAsync("Makrele", 7.00m).Returns(6.50m);
        var sut = CreateSutEditing();

        var offered = PickOnAdd(4);
        sut.AddPositionCommand.Execute(null);

        // The mackerel is in the order already, but can be added at another price; Bratwurst only has a
        // special price here, so it can be added at its normal price.
        Assert.Contains(offered(), p => p.ProductID == 4);
        Assert.Contains(offered(), p => p.ProductID == 1);

        var added = sut.EditPositions.Last();
        Assert.True(added.IsNew);
        Assert.Equal(OrderPositionKind.VariablePrice, added.Kind);
        Assert.Equal(6.50m, added.UnitPrice);
        Assert.Equal(7.00m, added.ListPrice);
    }

    [Fact]
    public void AddPosition_VariablePrice_Cancelled_AddsNothing()
    {
        SetUpSingleOrder();
        _dialogService.ShowPriceEntryAsync(Arg.Any<string>(), Arg.Any<decimal>(), Arg.Any<bool>()).Returns((decimal?)null);
        var sut = CreateSutEditing();

        PickOnAdd(4);
        sut.AddPositionCommand.Execute(null);

        Assert.Single(sut.EditPositions);
    }

    [Fact]
    public void AddPosition_FreeEntry_AddsNewFreeEntry()
    {
        SetUpSingleOrder();
        _orderService.GetRecentNotes(Arg.Any<int>()).Returns(new List<string> { "Kuchenspende" });
        _dialogService.ShowFreeEntryAsync(Arg.Is<IReadOnlyList<string>>(n => n.Contains("Kuchenspende")), Arg.Any<IReadOnlyList<ProductModel>>())
            .Returns(new FreeEntryResult(3.00m, "Kuchenspende", null));
        var sut = CreateSutEditing();

        PickOnAdd(null);
        sut.AddPositionCommand.Execute(null);

        var added = sut.EditPositions.Last();
        Assert.True(added.IsNew);
        Assert.Equal(OrderPositionKind.FreeEntry, added.Kind);
        Assert.Null(added.ProductId);
        Assert.Equal("Kuchenspende", added.DisplayName);
        Assert.Equal(3.00m, added.UnitPrice);
        Assert.Equal(10.00m, sut.EditTotal); // 7.00 + 3.00
    }

    [Fact]
    public void Save_MergesRowsThatEndedUpAlike()
    {
        SetUpOrderWithEnteredPrices();
        // A second mackerel at the same price as the one already there.
        _dialogService.ShowPriceEntryAsync("Makrele", 7.00m).Returns(8.50m);
        var sut = CreateSutEditing();
        PickOnAdd(4);
        sut.AddPositionCommand.Execute(null);

        sut.SaveCommand.Execute(null);

        _orderService.Received(1).ReplaceOrderPositions(Arg.Is<Order>(o =>
            o.Positions.Count == 3 &&
            o.Positions.Single(p => p.ProductId == 4).Amount == 3));
    }
}
