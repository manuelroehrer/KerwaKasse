using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.MVVM.Model;
using KerwaKasse.MVVM.ViewModel;
using NSubstitute;

namespace KerwaKasse.Tests.ViewModels;

public class OrderHistoryViewModelTests
{
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly ISettingsService _settings;

    public OrderHistoryViewModelTests()
    {
        _orderService = Substitute.For<IOrderService>();
        _productService = Substitute.For<IProductService>();
        _settings = Substitute.For<ISettingsService>();

        _orderService.GetOrdersByDate(Arg.Any<DateTime>()).Returns(new List<Order>());
        _settings.Get("productsSwatchUseColoredBorder", true).Returns(true);
        _settings.Get("productsSwatchBorderDarkenFactor", 0.7).Returns(0.7);
        _productService.GetAll().Returns(new List<Product>
        {
            new() { Id = 1, Name = "Bratwurst", Price = 3.50m },
            new() { Id = 2, Name = "Bier", Price = 2.80m },
            new() { Id = 3, Name = "Brezel", Price = 1.20m }
        });
    }

    private OrderHistoryViewModel CreateSut() => new(_orderService, _productService, _settings);

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

        Assert.True(sut.EditPositions[0].IsAmountChanged);
        Assert.True(sut.EditPositions[0].IsChanged);
    }

    [Fact]
    public void AddPosition_ViaPicker_AddsNewHighlightedPosition()
    {
        SetUpSingleOrder();
        var sut = CreateSut();
        sut.SelectedOrder = sut.Orders[0];
        sut.BeginEditCommand.Execute(null);

        // The picker offers everything except the already-present Bratwurst (1).
        sut.ShowAddPositionPicker = (candidates, onPicked) =>
        {
            Assert.DoesNotContain(candidates, p => p.ProductID == 1);
            onPicked(candidates.First(p => p.ProductID == 2)); // Bier
        };
        sut.AddPositionCommand.Execute(null);

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
        sut.ShowAddPositionPicker = (candidates, onPicked) => onPicked(candidates.First(p => p.ProductID == 2));
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
        sut.ShowAddPositionPicker = (candidates, onPicked) => onPicked(candidates.First(p => p.ProductID == 2));
        sut.AddPositionCommand.Execute(null);

        sut.SaveCommand.Execute(null);

        _orderService.Received(1).ReplaceOrderPositions(Arg.Is<Order>(o =>
            o.Id == 1 &&
            o.Positions.Count == 2 &&
            o.Positions.Any(p => p.ProductId == 1 && p.UnitPrice == 3.50m) &&
            o.Positions.Any(p => p.ProductId == 2 && p.UnitPrice == 2.80m)));
        Assert.False(sut.IsEditing);
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
}
