using Dapper;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using Microsoft.Data.Sqlite;

namespace KerwaKasse.Core.Data;

public class SqliteProductService : IProductService
{
    private readonly string _connectionString;

    public SqliteProductService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public List<Product> GetAll()
    {
        using var connection = new SqliteConnection(_connectionString);
        return connection.Query<ProductRow>(
            "SELECT Id, Description, PriceCents, Available, Color, SortOrder FROM Products ORDER BY SortOrder"
        ).Select(ToProduct).ToList();
    }

    public List<Product> GetAvailable()
    {
        using var connection = new SqliteConnection(_connectionString);
        return connection.Query<ProductRow>(
            "SELECT Id, Description, PriceCents, Available, Color, SortOrder FROM Products WHERE Available = 1 ORDER BY SortOrder"
        ).Select(ToProduct).ToList();
    }

    public void Add(Product product)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Execute(
            """
            INSERT INTO Products (Description, PriceCents, Available, Color, SortOrder)
            VALUES (@Description, @PriceCents, @Available, @Color, COALESCE((SELECT MAX(SortOrder) + 1 FROM Products), 1))
            """,
            new { product.Description, PriceCents = ToCents(product.Price), product.Available, product.Color });
    }

    public void Update(Product product)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Execute(
            """
            UPDATE Products SET Description = @Description, PriceCents = @PriceCents, Available = @Available, Color = @Color, SortOrder = @SortOrder
            WHERE Id = @Id
            """,
            new { product.Id, product.Description, PriceCents = ToCents(product.Price), product.Available, product.Color, product.SortOrder });
    }

    public void UpdateAvailability(int productId, bool available)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Execute(
            "UPDATE Products SET Available = @available WHERE Id = @productId",
            new { available, productId });
    }

    public void UpdateSortOrder(IEnumerable<Product> products)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var product in products)
        {
            connection.Execute(
                "UPDATE Products SET SortOrder = @SortOrder WHERE Id = @Id",
                new { product.SortOrder, product.Id },
                transaction);
        }

        transaction.Commit();
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
        Description = r.Description,
        Price = r.PriceCents / 100m,
        Available = r.Available,
        Color = r.Color,
        SortOrder = r.SortOrder
    };

    private class ProductRow
    {
        public int Id { get; set; }
        public string Description { get; set; } = string.Empty;
        public long PriceCents { get; set; }
        public bool Available { get; set; }
        public string? Color { get; set; }
        public int SortOrder { get; set; }
    }
}
