using KerwaKasse.Core.Data;
using KerwaKasse.Core.Models;
using Microsoft.Data.Sqlite;

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
        _sut = new SqliteProductService(_connectionString);
    }

    [Fact]
    public void Add_And_GetAll_RoundTrip()
    {
        var product = new Product { Description = "Bratwurst", Price = 3.50m, Available = true, Color = "#FF0000" };

        _sut.Add(product);

        var all = _sut.GetAll();
        Assert.Single(all);
        Assert.Equal("Bratwurst", all[0].Description);
        Assert.Equal(3.50m, all[0].Price);
        Assert.True(all[0].Available);
        Assert.Equal("#FF0000", all[0].Color);
    }

    [Fact]
    public void GetAvailable_FiltersUnavailableProducts()
    {
        _sut.Add(new Product { Description = "Bratwurst", Price = 3.50m, Available = true });
        _sut.Add(new Product { Description = "Pommes", Price = 2.50m, Available = false });

        var available = _sut.GetAvailable();
        Assert.Single(available);
        Assert.Equal("Bratwurst", available[0].Description);
    }

    [Fact]
    public void Update_ChangesProductFields()
    {
        _sut.Add(new Product { Description = "Bratwurst", Price = 3.50m, Available = true });
        var product = _sut.GetAll()[0];

        product.Price = 4.00m;
        product.Available = false;
        _sut.Update(product);

        var updated = _sut.GetAll()[0];
        Assert.Equal(4.00m, updated.Price);
        Assert.False(updated.Available);
    }

    [Fact]
    public void Update_ChangesDescription()
    {
        _sut.Add(new Product { Description = "Bratwurst", Price = 3.50m, Available = true });
        var product = _sut.GetAll()[0];

        product.Description = "Currywurst";
        _sut.Update(product);

        var updated = _sut.GetAll()[0];
        Assert.Equal("Currywurst", updated.Description);
    }

    [Fact]
    public void Add_AutoAssignsSortOrder()
    {
        _sut.Add(new Product { Description = "Bratwurst", Price = 3.50m });
        _sut.Add(new Product { Description = "Pommes", Price = 2.50m });

        var all = _sut.GetAll();
        Assert.Equal(1, all[0].SortOrder);
        Assert.Equal(2, all[1].SortOrder);
    }

    [Fact]
    public void UpdateSortOrder_ReordersProducts()
    {
        _sut.Add(new Product { Description = "A", Price = 1m });
        _sut.Add(new Product { Description = "B", Price = 2m });
        var all = _sut.GetAll();

        // Swap order
        all[0].SortOrder = 2;
        all[1].SortOrder = 1;
        _sut.UpdateSortOrder(all);

        var reordered = _sut.GetAll();
        Assert.Equal("B", reordered[0].Description); // SortOrder 1
        Assert.Equal("A", reordered[1].Description); // SortOrder 2
    }

    [Fact]
    public void GetAll_ReturnsOrderedBySortOrder()
    {
        _sut.Add(new Product { Description = "C", Price = 1m });
        _sut.Add(new Product { Description = "A", Price = 2m });
        _sut.Add(new Product { Description = "B", Price = 3m });

        var all = _sut.GetAll();
        // Should be ordered by SortOrder (1, 2, 3) which is insertion order
        Assert.Equal("C", all[0].Description);
        Assert.Equal("A", all[1].Description);
        Assert.Equal("B", all[2].Description);
    }

    public void Dispose()
    {
        _keepAlive.Dispose();
    }
}
