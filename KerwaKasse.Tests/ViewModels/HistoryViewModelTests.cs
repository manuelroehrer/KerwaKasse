using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using KerwaKasse.MVVM.ViewModel;
using NSubstitute;

namespace KerwaKasse.Tests.ViewModels;

public class HistoryViewModelTests
{
    private readonly IOrderService _orderService;
    private readonly HistoryViewModel _sut;

    public HistoryViewModelTests()
    {
        _orderService = Substitute.For<IOrderService>();
        _orderService.GetOrdersByDate(Arg.Any<DateTime>()).Returns(new List<Order>());

        _sut = new HistoryViewModel(_orderService);
    }

    [Fact]
    public void Constructor_SetsDateToToday()
    {
        Assert.Equal(DateTime.Today, _sut.Date);
    }

    [Fact]
    public void Constructor_LoadsOrdersForToday()
    {
        _orderService.Received().GetOrdersByDate(DateTime.Today);
    }

    [Fact]
    public void SetDate_TriggersLoadData()
    {
        _orderService.ClearReceivedCalls();

        _orderService.GetOrdersByDate(DateTime.Today.AddDays(-1)).Returns(new List<Order>());
        _sut.Date = DateTime.Today.AddDays(-1);

        _orderService.Received(1).GetOrdersByDate(DateTime.Today.AddDays(-1));
    }

    [Fact]
    public void LoadData_MapsOrdersCorrectly()
    {
        var testDate = DateTime.Today;
        _orderService.GetOrdersByDate(testDate).Returns(new List<Order>
        {
            new()
            {
                Id = 42,
                OrderTime = testDate.AddHours(12),
                Positions =
                [
                    new OrderPosition { ProductId = 1, ProductName = "Bratwurst", Amount = 2, UnitPrice = 3.50m }
                ]
            }
        });

        _sut.Reload();

        Assert.Single(_sut.Orders);
        Assert.Equal(42, _sut.Orders[0].OrderID);
        Assert.Single(_sut.Orders[0].OrderPositions);
    }

    [Fact]
    public void EditOrder_ViaDelegate_CallsUpdateOrderPositions()
    {
        var testDate = DateTime.Today;
        _orderService.GetOrdersByDate(testDate).Returns(new List<Order>
        {
            new()
            {
                Id = 1,
                OrderTime = testDate,
                Positions =
                [
                    new OrderPosition { ProductId = 1, ProductName = "Bratwurst", Amount = 1, UnitPrice = 3.50m }
                ]
            }
        });
        _sut.Reload();

        // Wire up the delegate to immediately invoke the save callback (simulates dialog save)
        _sut.ShowEditOrderDialog = (order, saveCallback) => saveCallback(order);

        _sut.EditOrderCommand.Execute(_sut.Orders[0]);

        _orderService.Received(1).UpdateOrderPositions(Arg.Is<Order>(o =>
            o.Id == 1 &&
            o.Positions.Count == 1 &&
            o.Positions[0].ProductId == 1));
    }

    [Fact]
    public void EditOrder_ViaDelegate_MapsAmountChanges()
    {
        var testDate = DateTime.Today;
        _orderService.GetOrdersByDate(testDate).Returns(new List<Order>
        {
            new()
            {
                Id = 1,
                OrderTime = testDate,
                Positions =
                [
                    new OrderPosition { ProductId = 1, ProductName = "Bratwurst", Amount = 2, UnitPrice = 3.50m }
                ]
            }
        });
        _sut.Reload();

        // Simulate edit: change amount in the delegate, then save
        _sut.ShowEditOrderDialog = (order, saveCallback) =>
        {
            order.OrderPositions[0].Amount = 5;
            saveCallback(order);
        };

        _sut.EditOrderCommand.Execute(_sut.Orders[0]);

        _orderService.Received(1).UpdateOrderPositions(Arg.Is<Order>(o =>
            o.Positions[0].Amount == 5));
    }
}
