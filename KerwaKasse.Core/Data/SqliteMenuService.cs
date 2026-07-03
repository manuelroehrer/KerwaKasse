using Dapper;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KerwaKasse.Core.Data;

public class SqliteMenuService : IMenuService
{
    private readonly string _connectionString;
    private readonly ILogger<SqliteMenuService> _logger;

    public SqliteMenuService(string connectionString, ILogger<SqliteMenuService> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
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
        int menuId = (int)connection.ExecuteScalar<long>("SELECT last_insert_rowid()");

        _logger.LogInformation("Menu {MenuId} (\"{MenuName}\") created", menuId, name);
        return menuId;
    }

    public void Rename(int menuId, string newName)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Execute(
            "UPDATE Menus SET Name = @newName WHERE Id = @menuId",
            new { menuId, newName });

        _logger.LogInformation("Menu {MenuId} renamed to \"{MenuName}\"", menuId, newName);
    }

    public void Delete(int menuId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        var menuName = GetName(connection, menuId);
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM MenuProducts WHERE MenuId = @menuId", new { menuId }, transaction);
        connection.Execute("DELETE FROM Menus WHERE Id = @menuId", new { menuId }, transaction);
        transaction.Commit();

        _logger.LogInformation("Menu {MenuId} (\"{MenuName}\") deleted", menuId, menuName);
    }

    public void SetProducts(int menuId, IEnumerable<int> productIds)
    {
        var productIdList = productIds.ToList();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("DELETE FROM MenuProducts WHERE MenuId = @menuId", new { menuId }, transaction);
        foreach (var productId in productIdList)
        {
            connection.Execute(
                "INSERT INTO MenuProducts (MenuId, ProductId) VALUES (@menuId, @productId)",
                new { menuId, productId }, transaction);
        }
        transaction.Commit();

        _logger.LogInformation("Menu {MenuId} (\"{MenuName}\"): {ProductCount} products assigned",
            menuId, GetName(connection, menuId), productIdList.Count);
    }

    public void UpdateSortOrder(IEnumerable<Menu> menus)
    {
        var menuList = menus.ToList();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        foreach (var menu in menuList)
        {
            connection.Execute(
                "UPDATE Menus SET SortOrder = @SortOrder WHERE Id = @Id",
                new { menu.SortOrder, menu.Id }, transaction);
        }
        transaction.Commit();

        _logger.LogInformation("Sort order of {MenuCount} menus updated", menuList.Count);
    }

    public void ApplyMenu(int menuId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute("UPDATE Products SET Available = 0", transaction: transaction);
        int availableCount = connection.Execute(
            """
            UPDATE Products SET Available = 1
            WHERE Id IN (SELECT ProductId FROM MenuProducts WHERE MenuId = @menuId)
            """,
            new { menuId }, transaction);
        transaction.Commit();

        _logger.LogInformation("Menu {MenuId} (\"{MenuName}\") applied: {ProductCount} products set to available",
            menuId, GetName(connection, menuId), availableCount);
    }

    /// <summary>Menu name looked up purely for log entries where the caller only knows the id.</summary>
    private static string? GetName(SqliteConnection connection, int menuId) =>
        connection.ExecuteScalar<string>("SELECT Name FROM Menus WHERE Id = @menuId", new { menuId });
}
