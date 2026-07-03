using KerwaKasse.Core.Data;
using KerwaKasse.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

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
        _sut = new SqliteOrderService(_connectionString, NullLogger<SqliteOrderService>.Instance);
        _productService = new SqliteProductService(_connectionString, NullLogger<SqliteProductService>.Instance);
    }

    private Product AddTestProduct(string desc = "Bratwurst", decimal price = 3.50m)
    {
        _productService.Add(new Product { Name = desc, Price = price, Available = true });
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
                UnitPrice = product.Price
            }
        };

        _sut.PlaceOrder(positions);

        var orders = _sut.GetOrdersByDate(DateTime.Today);
        Assert.Single(orders);
        Assert.Single(orders[0].Positions);
        Assert.Equal(2, orders[0].Positions[0].Amount);
        Assert.Equal(product.Price, orders[0].Positions[0].UnitPrice);
        // Name is resolved from the current product via JOIN, not from a snapshot.
        Assert.Equal("Bratwurst", orders[0].Positions[0].ProductName);
    }

    [Fact]
    public void PlaceOrder_WithMultiplePositions()
    {
        var p1 = AddTestProduct("Bratwurst", 3.50m);
        var p2 = AddTestProduct("Bier", 2.80m);

        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = p1.Id, Amount = 1, UnitPrice = p1.Price },
            new OrderPosition { ProductId = p2.Id, Amount = 3, UnitPrice = p2.Price }
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
            new OrderPosition { ProductId = product.Id, Amount = 1, UnitPrice = product.Price }
        });

        var orders = _sut.GetOrdersByDate(DateTime.Today.AddDays(-1));
        Assert.Empty(orders);
    }

    [Fact]
    public void GetOrdersByDate_FindsOrdersStoredInCanonicalDateFormat()
    {
        // OrderTime is stored as TEXT and filtered with a lexicographic BETWEEN, so the query
        // bounds must use the same canonical "yyyy-MM-dd HH:mm:ss" format as the stored values.
        // An ISO "o" bound (with a 'T' separator) would sort a space-separated timestamp below
        // the lower bound and hide it from the History/Statistics views.
        var product = AddTestProduct();
        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = product.Id, Amount = 1, UnitPrice = product.Price }
        });

        var historic = new DateTime(2022, 7, 29, 19, 44, 44);
        using (var cmd = _keepAlive.CreateCommand())
        {
            cmd.CommandText = "UPDATE Orders SET OrderTime = @t";
            cmd.Parameters.AddWithValue("@t", historic.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        var orders = _sut.GetOrdersByDate(historic.Date);

        Assert.Single(orders);
        Assert.Equal(historic, orders[0].OrderTime);
    }

    [Fact]
    public void GetSalesFigures_AggregatesCorrectly()
    {
        var p1 = AddTestProduct("Bratwurst", 3.50m);
        var p2 = AddTestProduct("Bier", 2.80m);

        // Two orders with the same products
        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = p1.Id, Amount = 2, UnitPrice = p1.Price },
            new OrderPosition { ProductId = p2.Id, Amount = 1, UnitPrice = p2.Price }
        });
        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = p1.Id, Amount = 3, UnitPrice = p1.Price }
        });

        var from = DateTime.Today;
        var to = DateTime.Today.AddDays(1);
        var figures = _sut.GetSalesFigures(from, to);

        var bratwurst = figures.First(f => f.ProductName == "Bratwurst");
        Assert.Equal(5, bratwurst.TotalAmount);       // 2 + 3
        Assert.Equal(17.50m, bratwurst.TotalRevenue);  // 5 * 3.50

        var bier = figures.First(f => f.ProductName == "Bier");
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
            new OrderPosition { ProductId = product.Id, Amount = 1, UnitPrice = product.Price }
        });

        var order = _sut.GetOrdersByDate(DateTime.Today)[0];
        Assert.Equal(DateTime.Today.Date, order.OrderTime.Date);
    }

    [Fact]
    public void RenamedProduct_HistoryAndStatistics_UseCurrentName()
    {
        // A product is sold, then renamed (e.g. a typo correction), then sold again.
        var product = AddTestProduct("Currywurst+Pommes", 7.50m);
        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = product.Id, Amount = 1, UnitPrice = product.Price }
        });

        product.Name = "Currywurst und Pommes";
        _productService.Update(product);

        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = product.Id, Amount = 2, UnitPrice = product.Price }
        });

        // History reflects the CURRENT name for every order, old and new.
        var orders = _sut.GetOrdersByDate(DateTime.Today);
        Assert.Equal(2, orders.Count);
        Assert.All(orders.SelectMany(o => o.Positions),
            p => Assert.Equal("Currywurst und Pommes", p.ProductName));

        // Statistics aggregate into a single row per ProductId (not split by name).
        var figures = _sut.GetSalesFigures(DateTime.Today, DateTime.Today.AddDays(1));
        var row = Assert.Single(figures);
        Assert.Equal(product.Id, row.ProductId);
        Assert.Equal("Currywurst und Pommes", row.ProductName);
        Assert.Equal(3, row.TotalAmount);        // 1 + 2
        Assert.Equal(22.50m, row.TotalRevenue);  // 3 * 7.50
    }

    [Fact]
    public void GetSalesFigures_MoneyIsExact_NoFloatDrift()
    {
        // 0.10 is not exactly representable as a float; with integer-cent storage the
        // summed revenue stays exact (this is the whole point of the cent switch).
        var product = AddTestProduct("Brezel", 0.10m);
        for (int i = 0; i < 3; i++)
            _sut.PlaceOrder(new[] { new OrderPosition { ProductId = product.Id, Amount = 1, UnitPrice = product.Price } });

        var figures = _sut.GetSalesFigures(DateTime.Today, DateTime.Today.AddDays(1));
        var row = Assert.Single(figures);
        Assert.Equal(3, row.TotalAmount);
        Assert.Equal(0.30m, row.TotalRevenue);
    }

    [Fact]
    public void ReplaceOrderPositions_AddsRemovesAndChangesAmounts()
    {
        var p1 = AddTestProduct("Bratwurst", 3.50m);
        var p2 = AddTestProduct("Bier", 2.80m);
        var p3 = AddTestProduct("Brezel", 1.20m);

        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = p1.Id, Amount = 2, UnitPrice = p1.Price },
            new OrderPosition { ProductId = p2.Id, Amount = 1, UnitPrice = p2.Price }
        });

        var order = _sut.GetOrdersByDate(DateTime.Today)[0];

        // Change p1's amount, drop p2, add p3.
        order.Positions = new List<OrderPosition>
        {
            new() { OrderId = order.Id, ProductId = p1.Id, Amount = 5, UnitPrice = p1.Price },
            new() { OrderId = order.Id, ProductId = p3.Id, Amount = 3, UnitPrice = p3.Price }
        };
        _sut.ReplaceOrderPositions(order);

        var updated = _sut.GetOrdersByDate(DateTime.Today)[0];
        Assert.Equal(2, updated.Positions.Count);
        Assert.Equal(5, updated.Positions.Single(p => p.ProductId == p1.Id).Amount);
        Assert.DoesNotContain(updated.Positions, p => p.ProductId == p2.Id);
        Assert.Equal(3, updated.Positions.Single(p => p.ProductId == p3.Id).Amount);
    }

    [Fact]
    public void ReplaceOrderPositions_KeepsSnapshottedUnitPrice()
    {
        var product = AddTestProduct("Bratwurst", 3.50m);
        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = product.Id, Amount = 1, UnitPrice = 3.50m }
        });

        var order = _sut.GetOrdersByDate(DateTime.Today)[0];
        order.Positions[0].Amount = 4;
        _sut.ReplaceOrderPositions(order);

        var updated = _sut.GetOrdersByDate(DateTime.Today)[0];
        Assert.Equal(3.50m, updated.Positions[0].UnitPrice);
    }

    [Fact]
    public void DeleteOrder_RemovesOrderAndPositions()
    {
        var product = AddTestProduct();
        _sut.PlaceOrder(new[]
        {
            new OrderPosition { ProductId = product.Id, Amount = 2, UnitPrice = product.Price }
        });

        var order = _sut.GetOrdersByDate(DateTime.Today)[0];
        _sut.DeleteOrder(order.Id);

        Assert.Empty(_sut.GetOrdersByDate(DateTime.Today));
        // The deleted order no longer contributes to the sales figures either.
        Assert.Empty(_sut.GetSalesFigures(DateTime.Today, DateTime.Today.AddDays(1)));
    }

    [Fact]
    public void DeleteOrder_LeavesOtherOrdersUntouched()
    {
        var product = AddTestProduct();
        _sut.PlaceOrder(new[] { new OrderPosition { ProductId = product.Id, Amount = 1, UnitPrice = product.Price } });
        _sut.PlaceOrder(new[] { new OrderPosition { ProductId = product.Id, Amount = 2, UnitPrice = product.Price } });

        var orders = _sut.GetOrdersByDate(DateTime.Today);
        _sut.DeleteOrder(orders[0].Id);

        var remaining = _sut.GetOrdersByDate(DateTime.Today);
        Assert.Single(remaining);
        Assert.Equal(orders[1].Id, remaining[0].Id);
    }

    public void Dispose()
    {
        _keepAlive.Dispose();
    }
}
