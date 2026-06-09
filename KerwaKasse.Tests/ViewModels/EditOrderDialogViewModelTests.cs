using KerwaKasse.MVVM.Model;
using KerwaKasse.MVVM.ViewModel;

namespace KerwaKasse.Tests.ViewModels;

public class EditOrderDialogViewModelTests
{
    private OrderModel CreateTestOrder()
    {
        var product = new ProductModel { ProductID = 1, Name = "Bratwurst", Price = 3.50m };
        return new OrderModel
        {
            OrderID = 42,
            OrderTime = DateTime.Now,
            OrderPositions = new List<OrderPositionModel>
            {
                new() { Product = product, Amount = 2, Total = 7.00m }
            },
            Total = 7.00m,
            ShortDescription = "Bratwurst"
        };
    }

    [Fact]
    public void SaveCommand_InvokesCallback()
    {
        var order = CreateTestOrder();
        OrderModel? savedOrder = null;
        var sut = new EditOrderDialogViewModel(order, o => savedOrder = o);
        sut.CloseDialog = () => { };

        sut.SaveCommand.Execute(null);

        Assert.NotNull(savedOrder);
        Assert.Equal(42, savedOrder!.OrderID);
    }

    [Fact]
    public void SaveCommand_InvokesCloseDialog()
    {
        var order = CreateTestOrder();
        bool closed = false;
        var sut = new EditOrderDialogViewModel(order, _ => { });
        sut.CloseDialog = () => closed = true;

        sut.SaveCommand.Execute(null);

        Assert.True(closed);
    }

    [Fact]
    public void DiscardCommand_InvokesCloseDialog_WithoutSaving()
    {
        var order = CreateTestOrder();
        bool saved = false;
        bool closed = false;
        var sut = new EditOrderDialogViewModel(order, _ => saved = true);
        sut.CloseDialog = () => closed = true;

        sut.DiscardCommand.Execute(null);

        Assert.True(closed);
        Assert.False(saved);
    }

    [Fact]
    public void NumberBoxChangedCommand_UpdatesTotalAndDescription()
    {
        var order = CreateTestOrder();
        var sut = new EditOrderDialogViewModel(order, _ => { });

        order.OrderPositions[0].Amount = 3;
        sut.NumberBoxChangedCommand.Execute(null);

        Assert.Equal(10.50m, order.Total); // 3 * 3.50
    }

    [Fact]
    public void Order_Property_IsSetFromConstructor()
    {
        var order = CreateTestOrder();
        var sut = new EditOrderDialogViewModel(order, _ => { });

        Assert.Same(order, sut.Order);
    }
}
