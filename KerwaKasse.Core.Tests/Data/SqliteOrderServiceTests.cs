using KerwaKasse.Core.Data;
using KerwaKasse.Core.Models;
using Microsoft.Data.Sqlite;

namespace KerwaKasse.Core.Tests.Data;

public class SqliteOrderServiceTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteOrderService _sut;
    private readonly SqliteProductService _productService;
    private readonly string _connectionString;

    public SqliteOrderServiceTests()
    {
        _connectionString = $"Data Source=OrderTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        DatabaseInitializer.Initialize(_connectionString);
        _sut = new SqliteOrderService(_connectionString);
        _productService = new SqliteProductService(_connectionString);
    }

    private Product AddTestProduct(string desc = "Bratwurst", decimal price = 3.50m)
    {
        _productService.Add(new Product { Description = desc, Price = price, Available = true });
        return _productService.GetAll().Last();
    }

    [Fact]
    public void PlaceOrder_And_GetOrdersByDate_RoundTrip()
    {
        var product = AddTestProduct();

        var positions = new[]
        {
            new OrderPosition
            {
                ProductId = product.Id,
                Amount = 2,
                UnitPrice = product.Price,
                ProductDescription = product.Description
            }
        };

        _sut.PlaceOrder(positions);

        var orders = _sut.GetOrdersByDate(DateTime.Today);
        Assert.Single(orders);
        Assert.Single(orders[0].Positions);
        Assert.Equal(2, orders[0].Positions[0].Amount);
        Assert.Equal(product.Price, orders[0].Positions[0].UnitPrice);
        Assert.Equal("Bratwurst", orders[0].Positions[0].ProductDescription);
    }

    [Fact]
    public void PlaceOrder_WithMultiplePositions()
    {
        var p1 = AddTestProduct("Bratwurst", 3.50m);
        var p2 = AddTestProduct("Bier", 2.80m);

        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = p1.Id, Amount = 1, UnitPrice = p1.Price, ProductDescription = p1.Description },
            new OrderPosition { ProductId = p2.Id, Amount = 3, UnitPrice = p2.Price, ProductDescription = p2.Description }
        });

        var orders = _sut.GetOrdersByDate(DateTime.Today);
        Assert.Single(orders);
        Assert.Equal(2, orders[0].Positions.Count);
    }

    [Fact]
    public void GetOrdersByDate_ReturnsEmptyForDifferentDate()
    {
        var product = AddTestProduct();
        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = product.Id, Amount = 1, UnitPrice = product.Price, ProductDescription = product.Description }
        });

        var orders = _sut.GetOrdersByDate(DateTime.Today.AddDays(-1));
        Assert.Empty(orders);
    }

    [Fact]
    public void UpdateOrderPositions_ChangesAmounts()
    {
        var product = AddTestProduct();
        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = product.Id, Amount = 2, UnitPrice = product.Price, ProductDescription = product.Description }
        });

        var order = _sut.GetOrdersByDate(DateTime.Today)[0];
        order.Positions[0].Amount = 5;
        _sut.UpdateOrderPositions(order);

        var updated = _sut.GetOrdersByDate(DateTime.Today)[0];
        Assert.Equal(5, updated.Positions[0].Amount);
    }

    [Fact]
    public void GetSalesFigures_AggregatesCorrectly()
    {
        var p1 = AddTestProduct("Bratwurst", 3.50m);
        var p2 = AddTestProduct("Bier", 2.80m);

        // Two orders with the same products
        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = p1.Id, Amount = 2, UnitPrice = p1.Price, ProductDescription = p1.Description },
            new OrderPosition { ProductId = p2.Id, Amount = 1, UnitPrice = p2.Price, ProductDescription = p2.Description }
        });
        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = p1.Id, Amount = 3, UnitPrice = p1.Price, ProductDescription = p1.Description }
        });

        var from = DateTime.Today;
        var to = DateTime.Today.AddDays(1);
        var figures = _sut.GetSalesFigures(from, to);

        var bratwurst = figures.First(f => f.ProductDescription == "Bratwurst");
        Assert.Equal(5, bratwurst.TotalAmount);       // 2 + 3
        Assert.Equal(17.50m, bratwurst.TotalRevenue);  // 5 * 3.50

        var bier = figures.First(f => f.ProductDescription == "Bier");
        Assert.Equal(1, bier.TotalAmount);
        Assert.Equal(2.80m, bier.TotalRevenue);
    }

    [Fact]
    public void GetSalesFigures_ReturnsEmptyForNoOrders()
    {
        var from = DateTime.Today;
        var to = DateTime.Today.AddDays(1);
        var figures = _sut.GetSalesFigures(from, to);

        Assert.Empty(figures);
    }

    [Fact]
    public void PlaceOrder_AssignsOrderTime()
    {
        var product = AddTestProduct();
        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = product.Id, Amount = 1, UnitPrice = product.Price, ProductDescription = product.Description }
        });

        var order = _sut.GetOrdersByDate(DateTime.Today)[0];
        Assert.Equal(DateTime.Today.Date, order.OrderTime.Date);
    }

    public void Dispose()
    {
        _keepAlive.Dispose();
    }
}
