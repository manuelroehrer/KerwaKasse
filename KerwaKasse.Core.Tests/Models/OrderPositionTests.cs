using KerwaKasse.Core.Models;

namespace KerwaKasse.Core.Tests.Models;

public class OrderPositionTests
{
    [Fact]
    public void Total_CalculatesAmountTimesUnitPrice()
    {
        var pos = new OrderPosition { Amount = 3, UnitPrice = 4.50m };

        Assert.Equal(13.50m, pos.Total);
    }

    [Fact]
    public void Total_WithZeroAmount_ReturnsZero()
    {
        var pos = new OrderPosition { Amount = 0, UnitPrice = 4.50m };

        Assert.Equal(0m, pos.Total);
    }

    [Fact]
    public void Total_WithDefaultValues_ReturnsUnitPrice()
    {
        // Amount defaults to 1
        var pos = new OrderPosition { UnitPrice = 7.00m };

        Assert.Equal(7.00m, pos.Total);
    }
}
