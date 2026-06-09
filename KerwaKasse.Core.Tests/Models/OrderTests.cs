using KerwaKasse.Core.Models;

namespace KerwaKasse.Core.Tests.Models;

public class OrderTests
{
    [Fact]
    public void Total_WithMultiplePositions_SumsCorrectly()
    {
        var order = new Order
        {
            Positions =
            [
                new OrderPosition { Amount = 2, UnitPrice = 3.50m },
                new OrderPosition { Amount = 1, UnitPrice = 5.00m }
            ]
        };

        Assert.Equal(12.00m, order.Total);
    }

    [Fact]
    public void Total_WithEmptyPositions_ReturnsZero()
    {
        var order = new Order();

        Assert.Equal(0m, order.Total);
    }

    [Fact]
    public void Total_WithSinglePosition_ReturnsPositionTotal()
    {
        var order = new Order
        {
            Positions = [new OrderPosition { Amount = 3, UnitPrice = 4.20m }]
        };

        Assert.Equal(12.60m, order.Total);
    }

    [Fact]
    public void ShortDescription_JoinsProductNames()
    {
        var order = new Order
        {
            Positions =
            [
                new OrderPosition { ProductName = "Bratwurst" },
                new OrderPosition { ProductName = "Bier" }
            ]
        };

        Assert.Equal("Bratwurst, Bier", order.ShortDescription);
    }

    [Fact]
    public void ShortDescription_WithSinglePosition_ReturnsSingleDescription()
    {
        var order = new Order
        {
            Positions = [new OrderPosition { ProductName = "Schnitzel" }]
        };

        Assert.Equal("Schnitzel", order.ShortDescription);
    }

    [Fact]
    public void ShortDescription_WithEmptyPositions_ReturnsEmptyString()
    {
        var order = new Order();

        Assert.Equal(string.Empty, order.ShortDescription);
    }
}
