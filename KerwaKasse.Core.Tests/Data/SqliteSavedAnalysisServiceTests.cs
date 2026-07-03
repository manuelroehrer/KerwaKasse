using KerwaKasse.Core.Data;
using KerwaKasse.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace KerwaKasse.Core.Tests.Data;

public class SqliteSavedAnalysisServiceTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteSavedAnalysisService _sut;
    private readonly SqliteProductService _products;

    public SqliteSavedAnalysisServiceTests()
    {
        var connectionString = $"Data Source=SavedAnalysisTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        DatabaseInitializer.Initialize(connectionString);
        _sut = new SqliteSavedAnalysisService(connectionString, NullLogger<SqliteSavedAnalysisService>.Instance);
        _products = new SqliteProductService(connectionString, NullLogger<SqliteProductService>.Instance);
    }

    private (int p1, int p2) AddTwoProducts()
    {
        _products.Add(new Product { Name = "Bratwurst", Price = 3.0m, Available = true });
        _products.Add(new Product { Name = "Bier", Price = 3.5m, Available = true });
        var all = _products.GetAll();
        return (all[0].Id, all[1].Id);
    }

    [Fact]
    public void Add_Template_RoundTrips()
    {
        int id = _sut.Add(new SavedAnalysis { Name = "Getränke", AllProducts = true });

        var saved = Assert.Single(_sut.GetAll());
        Assert.Equal(id, saved.Id);
        Assert.Equal("Getränke", saved.Name);
        Assert.True(saved.AllProducts);
        Assert.Equal(0, saved.ProductCount);
    }

    [Fact]
    public void Add_WithProductSelection_StoresIdsAndCount()
    {
        var (p1, p2) = AddTwoProducts();

        int id = _sut.Add(new SavedAnalysis
        {
            Name = "Nur Essen",
            AllProducts = false,
            ProductIds = new List<int> { p1, p2 }
        });

        Assert.Equal(new[] { p1, p2 }, _sut.GetProductIds(id).OrderBy(x => x));
        Assert.Equal(2, _sut.GetAll().Single().ProductCount);
    }

    [Fact]
    public void Add_WithDateRange_RoundTripsDates()
    {
        var from = new DateTime(2025, 7, 27, 6, 0, 0);
        var to = new DateTime(2025, 7, 27, 14, 0, 0);

        _sut.Add(new SavedAnalysis
        {
            Name = "Sonntag Frühschoppen",
            From = from,
            To = to
        });

        var saved = _sut.GetAll().Single();
        Assert.Equal(from, saved.From);
        Assert.Equal(to, saved.To);
    }

    [Fact]
    public void Update_ReplacesFieldsAndProducts()
    {
        var (p1, p2) = AddTwoProducts();
        int id = _sut.Add(new SavedAnalysis { Name = "Alt", AllProducts = false, ProductIds = new List<int> { p1 } });

        _sut.Update(new SavedAnalysis
        {
            Id = id,
            Name = "Neu",
            AllProducts = false,
            ProductIds = new List<int> { p2 }
        });

        var saved = _sut.GetAll().Single();
        Assert.Equal("Neu", saved.Name);
        Assert.Equal(new[] { p2 }, _sut.GetProductIds(id));
    }

    [Fact]
    public void Update_SwitchingToAllProducts_ClearsSelection()
    {
        var (p1, _) = AddTwoProducts();
        int id = _sut.Add(new SavedAnalysis { Name = "X", AllProducts = false, ProductIds = new List<int> { p1 } });

        _sut.Update(new SavedAnalysis { Id = id, Name = "X", AllProducts = true });

        Assert.Empty(_sut.GetProductIds(id));
        Assert.Equal(0, _sut.GetAll().Single().ProductCount);
    }

    [Fact]
    public void Rename_ChangesNameOnly()
    {
        int id = _sut.Add(new SavedAnalysis { Name = "Tippfeler" });
        _sut.Rename(id, "Tippfehler");
        Assert.Equal("Tippfehler", _sut.GetAll().Single().Name);
    }

    [Fact]
    public void Delete_RemovesAnalysisAndProducts()
    {
        var (p1, _) = AddTwoProducts();
        int id = _sut.Add(new SavedAnalysis { Name = "Weg", AllProducts = false, ProductIds = new List<int> { p1 } });

        _sut.Delete(id);

        Assert.Empty(_sut.GetAll());
        Assert.Empty(_sut.GetProductIds(id));
    }

    [Fact]
    public void Add_AppendsInInsertionOrder()
    {
        _sut.Add(new SavedAnalysis { Name = "Zebra" });
        _sut.Add(new SavedAnalysis { Name = "Apfel" });

        Assert.Equal(new[] { "Zebra", "Apfel" }, _sut.GetAll().Select(a => a.Name));
    }

    [Fact]
    public void UpdateSortOrder_Reorders()
    {
        int zebra = _sut.Add(new SavedAnalysis { Name = "Zebra" });
        int apfel = _sut.Add(new SavedAnalysis { Name = "Apfel" });

        _sut.UpdateSortOrder(new[]
        {
            new SavedAnalysis { Id = apfel, SortOrder = 1 },
            new SavedAnalysis { Id = zebra, SortOrder = 2 }
        });

        Assert.Equal(new[] { "Apfel", "Zebra" }, _sut.GetAll().Select(a => a.Name));
    }

    public void Dispose()
    {
        _keepAlive.Dispose();
    }
}
