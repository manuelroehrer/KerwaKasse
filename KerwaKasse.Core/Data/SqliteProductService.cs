using Dapper;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KerwaKasse.Core.Data;

public class SqliteProductService : IProductService
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteProductService> _logger;

    public SqliteProductService(string connectionString, ILogger<SqliteProductService> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public List<Product> GetAll()
    {
        using var connection = new SqliteConnection(_connectionString);
        return connection.Query<ProductRow>(
            "SELECT Id, Name, PriceCents, Available, Color, SortOrder FROM Products ORDER BY SortOrder"
        ).Select(ToProduct).ToList();
    }

    public List<Product> GetAvailable()
    {
        using var connection = new SqliteConnection(_connectionString);
        return connection.Query<ProductRow>(
            "SELECT Id, Name, PriceCents, Available, Color, SortOrder FROM Products WHERE Available = 1 ORDER BY SortOrder"
        ).Select(ToProduct).ToList();
    }

    public void Add(Product product)
    {
        using var connection = new SqliteConnection(_connectionString);
        var productId = connection.ExecuteScalar<long>(
            """
            INSERT INTO Products (Name, PriceCents, Available, Color, SortOrder)
            VALUES (@Name, @PriceCents, @Available, @Color, COALESCE((SELECT MAX(SortOrder) + 1 FROM Products), 1));
            SELECT last_insert_rowid();
            """,
            new { product.Name, PriceCents = ToCents(product.Price), product.Available, product.Color });

        _logger.LogInformation("Product {ProductId} (\"{ProductName}\") created: price {Price:0.00} €, available: {Available}",
            productId, product.Name, product.Price, product.Available);
    }

    public void Update(Product product)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Execute(
            """
            UPDATE Products SET Name = @Name, PriceCents = @PriceCents, Available = @Available, Color = @Color, SortOrder = @SortOrder
            WHERE Id = @Id
            """,
            new { product.Id, product.Name, PriceCents = ToCents(product.Price), product.Available, product.Color, product.SortOrder });

        _logger.LogInformation("Product {ProductId} (\"{ProductName}\") updated: price {Price:0.00} €, available: {Available}",
            product.Id, product.Name, product.Price, product.Available);
    }

    public void UpdateAvailability(int productId, bool available)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Execute(
            "UPDATE Products SET Available = @available WHERE Id = @productId",
            new { available, productId });

        // The name is looked up purely for the log entry; the caller only knows the id.
        var productName = connection.ExecuteScalar<string>(
            "SELECT Name FROM Products WHERE Id = @productId", new { productId });
        _logger.LogInformation("Product {ProductId} (\"{ProductName}\") availability changed: {Available}",
            productId, productName, available);
    }

    public void UpdateSortOrder(IEnumerable<Product> products)
    {
        var productList = products.ToList();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var product in productList)
        {
            connection.Execute(
                "UPDATE Products SET SortOrder = @SortOrder WHERE Id = @Id",
                new { product.SortOrder, product.Id },
                transaction);
        }

        transaction.Commit();

        _logger.LogInformation("Sort order of {ProductCount} products updated", productList.Count);
    }

    public void MoveAvailable(int productId, int newIndex)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var all = connection.Query<ProductRow>(
            "SELECT Id, Name, PriceCents, Available, Color, SortOrder FROM Products ORDER BY SortOrder",
            transaction: transaction).ToList();
        var available = all.Where(p => p.Available).Select(p => p.Id).ToList();
        int oldIndex = available.IndexOf(productId);
        if (oldIndex < 0) return;

        newIndex = Math.Clamp(newIndex, 0, available.Count - 1);
        available.RemoveAt(oldIndex);
        available.Insert(newIndex, productId);

        // Hand the places of the available products out again in their new order.
        int next = 0;
        for (int i = 0; i < all.Count; i++)
        {
            int id = all[i].Available ? available[next++] : all[i].Id;
            connection.Execute(
                "UPDATE Products SET SortOrder = @SortOrder WHERE Id = @Id",
                new { SortOrder = i + 1, Id = id },
                transaction);
        }

        transaction.Commit();

        _logger.LogInformation("Product {ProductId} moved from position {OldIndex} to {NewIndex} among {AvailableCount} available products",
            productId, oldIndex + 1, newIndex + 1, available.Count);
    }

    public int GetUsageCount(int productId)
    {
        using var connection = new SqliteConnection(_connectionString);
        return connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM OrderPositions WHERE ProductId = @productId",
            new { productId });
    }

    private static long ToCents(decimal price) => (long)Math.Round(price * 100m, MidpointRounding.AwayFromZero);

    private static Product ToProduct(ProductRow r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        Price = r.PriceCents / 100m,
        Available = r.Available,
        Color = r.Color,
        SortOrder = r.SortOrder
    };

    private class ProductRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public long PriceCents { get; set; }
        public bool Available { get; set; }
        public string? Color { get; set; }
        public int SortOrder { get; set; }
    }
}
