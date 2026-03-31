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
        return connection.Query<Product>(
            "SELECT Id, Description, Price, Available, Color, ImagePath, SortOrder FROM Product ORDER BY SortOrder"
        ).ToList();
    }

    public List<Product> GetAvailable()
    {
        using var connection = new SqliteConnection(_connectionString);
        return connection.Query<Product>(
            "SELECT Id, Description, Price, Available, Color, ImagePath, SortOrder FROM Product WHERE Available = 1 ORDER BY SortOrder"
        ).ToList();
    }

    public void Add(Product product)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Execute(
            """
            INSERT INTO Product (Description, Price, Available, Color, ImagePath, SortOrder)
            VALUES (@Description, @Price, @Available, @Color, @ImagePath, COALESCE((SELECT MAX(SortOrder) + 1 FROM Product), 1))
            """,
            product);
    }

    public void Update(Product product)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Execute(
            """
            UPDATE Product SET Description = @Description, Price = @Price, Available = @Available, Color = @Color, ImagePath = @ImagePath, SortOrder = @SortOrder
            WHERE Id = @Id
            """,
            product);
    }

    public void UpdateSortOrder(IEnumerable<Product> products)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var product in products)
        {
            connection.Execute(
                "UPDATE Product SET SortOrder = @SortOrder WHERE Id = @Id",
                new { product.SortOrder, product.Id },
                transaction);
        }

        transaction.Commit();
    }
}
