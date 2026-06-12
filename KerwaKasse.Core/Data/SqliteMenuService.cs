using Dapper;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using Microsoft.Data.Sqlite;

namespace KerwaKasse.Core.Data;

public class SqliteMenuService : IMenuService
{
    private readonly string _connectionString;

    public SqliteMenuService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public List<Menu> GetAll()
    {
        using var connection = new SqliteConnection(_connectionString);
        return connection.Query<Menu>("SELECT Id, Name, SortOrder FROM Menus ORDER BY SortOrder, Name").ToList();
    }

    public List<int> GetProductIds(int menuId)
    {
        using var connection = new SqliteConnection(_connectionString);
        return connection.Query<int>(
            "SELECT ProductId FROM MenuProducts WHERE MenuId = @menuId",
            new { menuId }).ToList();
    }

    public int Add(string name)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute(
            "INSERT INTO Menus (Name, SortOrder) VALUES (@name, (SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM Menus))",
            new { name });
        return (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");
    }

    public void Rename(int menuId, string newName)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Execute(
            "UPDATE Menus SET Name = @newName WHERE Id = @menuId",
            new { menuId, newName });
    }

    public void Delete(int menuId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM MenuProducts WHERE MenuId = @menuId", new { menuId }, transaction);
        connection.Execute("DELETE FROM Menus WHERE Id = @menuId", new { menuId }, transaction);
        transaction.Commit();
    }

    public void SetProducts(int menuId, IEnumerable<int> productIds)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM MenuProducts WHERE MenuId = @menuId", new { menuId }, transaction);
        foreach (var productId in productIds)
        {
            connection.Execute(
                "INSERT INTO MenuProducts (MenuId, ProductId) VALUES (@menuId, @productId)",
                new { menuId, productId }, transaction);
        }
        transaction.Commit();
    }

    public void UpdateSortOrder(IEnumerable<Menu> menus)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var menu in menus)
        {
            connection.Execute(
                "UPDATE Menus SET SortOrder = @SortOrder WHERE Id = @Id",
                new { menu.SortOrder, menu.Id }, transaction);
        }
        transaction.Commit();
    }

    public void ApplyMenu(int menuId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("UPDATE Products SET Available = 0", transaction: transaction);
        connection.Execute(
            """
            UPDATE Products SET Available = 1
            WHERE Id IN (SELECT ProductId FROM MenuProducts WHERE MenuId = @menuId)
            """,
            new { menuId }, transaction);
        transaction.Commit();
    }
}
