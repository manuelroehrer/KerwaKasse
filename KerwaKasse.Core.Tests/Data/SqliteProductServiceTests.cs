using KerwaKasse.Core.Data;
using KerwaKasse.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace KerwaKasse.Core.Tests.Data;

public class SqliteProductServiceTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteProductService _sut;
    private readonly string _connectionString;

    public SqliteProductServiceTests()
    {
        _connectionString = $"Data Source=ProdTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(_connectionString);
        _keepAlive.Open();
        DatabaseInitializer.Initialize(_connectionString);
        _sut = new SqliteProductService(_connectionString, NullLogger<SqliteProductService>.Instance);
    }

    [Fact]
    public void Add_And_GetAll_RoundTrip()
    {
        var product = new Product { Name = "Bratwurst", Price = 3.50m, Available = true, Color = "#FF0000" };

        _sut.Add(product);

        var all = _sut.GetAll();
        Assert.Single(all);
        Assert.Equal("Bratwurst", all[0].Name);
        Assert.Equal(3.50m, all[0].Price);
        Assert.True(all[0].Available);
        Assert.Equal("#FF0000", all[0].Color);
    }

    [Fact]
    public void GetAvailable_FiltersUnavailableProducts()
    {
        _sut.Add(new Product { Name = "Bratwurst", Price = 3.50m, Available = true });
        _sut.Add(new Product { Name = "Pommes", Price = 2.50m, Available = false });

        var available = _sut.GetAvailable();
        Assert.Single(available);
        Assert.Equal("Bratwurst", available[0].Name);
    }

    [Fact]
    public void Update_ChangesProductFields()
    {
        _sut.Add(new Product { Name = "Bratwurst", Price = 3.50m, Available = true });
        var product = _sut.GetAll()[0];

        product.Price = 4.00m;
        product.Available = false;
        _sut.Update(product);

        var updated = _sut.GetAll()[0];
        Assert.Equal(4.00m, updated.Price);
        Assert.False(updated.Available);
    }

    [Fact]
    public void Update_ChangesName()
    {
        _sut.Add(new Product { Name = "Bratwurst", Price = 3.50m, Available = true });
        var product = _sut.GetAll()[0];

        product.Name = "Currywurst";
        _sut.Update(product);

        var updated = _sut.GetAll()[0];
        Assert.Equal("Currywurst", updated.Name);
    }

    [Fact]
    public void Add_AutoAssignsSortOrder()
    {
        _sut.Add(new Product { Name = "Bratwurst", Price = 3.50m });
        _sut.Add(new Product { Name = "Pommes", Price = 2.50m });

        var all = _sut.GetAll();
        Assert.Equal(1, all[0].SortOrder);
        Assert.Equal(2, all[1].SortOrder);
    }

    [Fact]
    public void UpdateSortOrder_ReordersProducts()
    {
        _sut.Add(new Product { Name = "A", Price = 1m });
        _sut.Add(new Product { Name = "B", Price = 2m });
        var all = _sut.GetAll();

        // Swap order
        all[0].SortOrder = 2;
        all[1].SortOrder = 1;
        _sut.UpdateSortOrder(all);

        var reordered = _sut.GetAll();
        Assert.Equal("B", reordered[0].Name); // SortOrder 1
        Assert.Equal("A", reordered[1].Name); // SortOrder 2
    }

    [Fact]
    public void MoveAvailable_KeepsUnavailableProductsInPlace()
    {
        _sut.Add(new Product { Name = "Off1", Price = 1m, Available = false });
        _sut.Add(new Product { Name = "A", Price = 1m, Available = true });
        _sut.Add(new Product { Name = "Off2", Price = 1m, Available = false });
        _sut.Add(new Product { Name = "B", Price = 1m, Available = true });
        _sut.Add(new Product { Name = "C", Price = 1m, Available = true });
        int idC = _sut.GetAll().Single(p => p.Name == "C").Id;

        _sut.MoveAvailable(idC, 0); // to the front of the order panel

        Assert.Equal(new[] { "Off1", "C", "Off2", "A", "B" }, _sut.GetAll().Select(p => p.Name));
        Assert.Equal(new[] { "C", "A", "B" }, _sut.GetAvailable().Select(p => p.Name));
    }

    [Fact]
    public void MoveAvailable_SwapsAcrossHiddenProducts()
    {
        // Positions 2 and 3 are unavailable, so the order panel shows A and B side by side.
        _sut.Add(new Product { Name = "A", Price = 1m, Available = true });
        _sut.Add(new Product { Name = "Off1", Price = 1m, Available = false });
        _sut.Add(new Product { Name = "Off2", Price = 1m, Available = false });
        _sut.Add(new Product { Name = "B", Price = 1m, Available = true });
        int idB = _sut.GetAll().Single(p => p.Name == "B").Id;

        _sut.MoveAvailable(idB, 0); // swap the first two tiles

        Assert.Equal(new[] { "B", "Off1", "Off2", "A" }, _sut.GetAll().Select(p => p.Name));
    }

    [Fact]
    public void MoveAvailable_IgnoresUnavailableProduct()
    {
        _sut.Add(new Product { Name = "A", Price = 1m, Available = true });
        _sut.Add(new Product { Name = "Off", Price = 1m, Available = false });
        int idOff = _sut.GetAll().Single(p => p.Name == "Off").Id;

        _sut.MoveAvailable(idOff, 0);

        Assert.Equal(new[] { "A", "Off" }, _sut.GetAll().Select(p => p.Name));
    }

    [Fact]
    public void GetAll_ReturnsOrderedBySortOrder()
    {
        _sut.Add(new Product { Name = "C", Price = 1m });
        _sut.Add(new Product { Name = "A", Price = 2m });
        _sut.Add(new Product { Name = "B", Price = 3m });

        var all = _sut.GetAll();
        // Should be ordered by SortOrder (1, 2, 3) which is insertion order
        Assert.Equal("C", all[0].Name);
        Assert.Equal("A", all[1].Name);
        Assert.Equal("B", all[2].Name);
    }

    [Fact]
    public void UpdateAvailability_PersistsNewValue()
    {
        _sut.Add(new Product { Name = "Bratwurst", Price = 3.50m, Available = true });
        int id = _sut.GetAll()[0].Id;

        _sut.UpdateAvailability(id, false);

        Assert.False(_sut.GetAll()[0].Available);
    }

    [Fact]
    public void GetUsageCount_ZeroForUnusedProduct()
    {
        _sut.Add(new Product { Name = "Bratwurst", Price = 3.50m });
        int id = _sut.GetAll()[0].Id;

        Assert.Equal(0, _sut.GetUsageCount(id));
    }

    [Fact]
    public void GetUsageCount_CountsReferencingOrderPositions()
    {
        _sut.Add(new Product { Name = "Bratwurst", Price = 3.50m });
        int id = _sut.GetAll()[0].Id;

        var orders = new SqliteOrderService(_connectionString, NullLogger<SqliteOrderService>.Instance);
        orders.PlaceOrder(new[] { new OrderPosition { ProductId = id, Amount = 2, UnitPrice = 3.50m } });
        orders.PlaceOrder(new[] { new OrderPosition { ProductId = id, Amount = 1, UnitPrice = 3.50m } });

        Assert.Equal(2, _sut.GetUsageCount(id));
    }

    public void Dispose()
    {
        _keepAlive.Dispose();
    }
}
