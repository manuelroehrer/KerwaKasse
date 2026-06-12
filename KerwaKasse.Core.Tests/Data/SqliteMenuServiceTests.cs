using KerwaKasse.Core.Data;
using KerwaKasse.Core.Models;
using Microsoft.Data.Sqlite;

namespace KerwaKasse.Core.Tests.Data;

public class SqliteMenuServiceTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteMenuService _sut;
    private readonly SqliteProductService _products;

    public SqliteMenuServiceTests()
    {
        var connectionString = $"Data Source=MenuTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        DatabaseInitializer.Initialize(connectionString);
        _sut = new SqliteMenuService(connectionString);
        _products = new SqliteProductService(connectionString);
    }

    [Fact]
    public void Add_And_GetAll_RoundTrip()
    {
        int id = _sut.Add("Frühstück");

        var all = _sut.GetAll();
        Assert.Single(all);
        Assert.Equal(id, all[0].Id);
        Assert.Equal("Frühstück", all[0].Name);
    }

    [Fact]
    public void Rename_ChangesName()
    {
        int id = _sut.Add("Frühstük");

        _sut.Rename(id, "Frühstück");

        Assert.Equal("Frühstück", _sut.GetAll().Single().Name);
    }

    [Fact]
    public void SetProducts_And_GetProductIds_RoundTrip()
    {
        var (p1, p2, _) = AddThreeProducts();
        int menuId = _sut.Add("Abendessen");

        _sut.SetProducts(menuId, new[] { p1, p2 });

        var ids = _sut.GetProductIds(menuId);
        Assert.Equal(new[] { p1, p2 }, ids.OrderBy(x => x));
    }

    [Fact]
    public void SetProducts_ReplacesPreviousAssignment()
    {
        var (p1, p2, p3) = AddThreeProducts();
        int menuId = _sut.Add("Abendessen");

        _sut.SetProducts(menuId, new[] { p1, p2 });
        _sut.SetProducts(menuId, new[] { p3 });

        Assert.Equal(new[] { p3 }, _sut.GetProductIds(menuId));
    }

    [Fact]
    public void Delete_RemovesMenuAndAssignments()
    {
        var (p1, _, _) = AddThreeProducts();
        int menuId = _sut.Add("Sonntagskarte");
        _sut.SetProducts(menuId, new[] { p1 });

        _sut.Delete(menuId);

        Assert.Empty(_sut.GetAll());
        Assert.Empty(_sut.GetProductIds(menuId));
    }

    [Fact]
    public void ApplyMenu_SetsAvailabilityToMatchMenu()
    {
        var (p1, p2, p3) = AddThreeProducts();
        int menuId = _sut.Add("Abendessen");
        _sut.SetProducts(menuId, new[] { p1, p2 });

        _sut.ApplyMenu(menuId);

        var byId = _products.GetAll().ToDictionary(p => p.Id, p => p.Available);
        Assert.True(byId[p1]);
        Assert.True(byId[p2]);
        Assert.False(byId[p3]);
    }

    [Fact]
    public void Add_AppendsInInsertionOrder()
    {
        _sut.Add("Zebra");
        _sut.Add("Apfel");

        Assert.Equal(new[] { "Zebra", "Apfel" }, _sut.GetAll().Select(m => m.Name));
    }

    [Fact]
    public void UpdateSortOrder_ReordersMenus()
    {
        int zebra = _sut.Add("Zebra");
        int apfel = _sut.Add("Apfel");

        _sut.UpdateSortOrder(new[]
        {
            new Menu { Id = apfel, SortOrder = 1 },
            new Menu { Id = zebra, SortOrder = 2 },
        });

        Assert.Equal(new[] { "Apfel", "Zebra" }, _sut.GetAll().Select(m => m.Name));
    }

    /// <summary>Adds three available products and returns their generated ids in insertion order.</summary>
    private (int p1, int p2, int p3) AddThreeProducts()
    {
        _products.Add(new Product { Name = "Bratwurst", Price = 3.5m, Available = true });
        _products.Add(new Product { Name = "Pommes", Price = 2.5m, Available = true });
        _products.Add(new Product { Name = "Brezel", Price = 1.5m, Available = true });
        var all = _products.GetAll();
        return (all[0].Id, all[1].Id, all[2].Id);
    }

    public void Dispose()
    {
        _keepAlive.Dispose();
    }
}
