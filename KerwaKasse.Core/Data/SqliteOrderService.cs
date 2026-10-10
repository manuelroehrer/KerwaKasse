using System.Globalization;
using Dapper;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace KerwaKasse.Core.Data;

public class SqliteOrderService : IOrderService
{
    // Canonical text format for the OrderTime column. OrderTime is stored as TEXT and filtered
    // with a lexicographic BETWEEN, so every value must use the exact same format for the date
    // range queries below to work. This SQLite-native format sorts chronologically as text;
    // an ISO round-trip format (e.g. "o", with a 'T' separator) would not align with the bounds.
    private const string SqliteDateFormat = "yyyy-MM-dd HH:mm:ss";

    private readonly string _connectionString;
    private readonly ILogger<SqliteOrderService> _logger;

    public SqliteOrderService(string connectionString, ILogger<SqliteOrderService> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public void PlaceOrder(IEnumerable<OrderPosition> positions)
    {
        var positionList = positions.ToList();

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var orderId = connection.ExecuteScalar<int>(
            """INSERT INTO Orders (OrderTime) VALUES (@Now); SELECT last_insert_rowid();""",
            new { Now = DateTime.Now.ToString(SqliteDateFormat) },
            transaction);

        foreach (var pos in positionList)
            InsertPosition(connection, transaction, orderId, pos);

        transaction.Commit();

        _logger.LogInformation("Order {OrderId} created: {PositionCount} positions, total {Total:0.00} €",
            orderId, positionList.Count, positionList.Sum(p => p.Amount * p.UnitPrice));
    }

    public List<Order> GetOrdersByDate(DateTime date)
    {
        using var connection = new SqliteConnection(_connectionString);

        var dayStart = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0).ToString(SqliteDateFormat);
        var dayEnd = new DateTime(date.Year, date.Month, date.Day, 23, 59, 59).ToString(SqliteDateFormat);

        var rows = connection.Query<OrderPositionRow>(
            """
            SELECT o.Id AS OrderId, o.OrderTime, op.Id, op.ProductId, op.Kind, op.Amount, op.UnitPriceCents,
                   op.ListPriceCents, op.Note,
                   CASE WHEN op.ProductId IS NULL THEN '' ELSE COALESCE(p.Name, '(unbekanntes Produkt)') END AS ProductName
            FROM Orders o
            INNER JOIN OrderPositions op ON o.Id = op.OrderId
            LEFT JOIN Products p ON p.Id = op.ProductId
            WHERE o.OrderTime BETWEEN @DayStart AND @DayEnd
            ORDER BY o.Id, op.Id
            """,
            new { DayStart = dayStart, DayEnd = dayEnd });

        var orders = new Dictionary<int, Order>();

        foreach (var row in rows)
        {
            if (!orders.TryGetValue(row.OrderId, out var order))
            {
                order = new Order
                {
                    Id = row.OrderId,
                    OrderTime = DateTime.Parse(row.OrderTime, CultureInfo.InvariantCulture)
                };
                orders[row.OrderId] = order;
            }

            order.Positions.Add(new OrderPosition
            {
                Id = row.Id,
                OrderId = row.OrderId,
                ProductId = row.ProductId,
                Kind = (OrderPositionKind)row.Kind,
                ProductName = row.ProductName,
                Amount = row.Amount,
                UnitPrice = row.UnitPriceCents / 100m,
                ListPrice = row.ListPriceCents / 100m,
                Note = row.Note
            });
        }

        return orders.Values.ToList();
    }

    public void ReplaceOrderPositions(Order order)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        // Full replace: drop the order's current positions and re-insert the desired set. This
        // covers added, removed and changed positions uniformly; the positions get new ids.
        connection.Execute(
            "DELETE FROM OrderPositions WHERE OrderId = @Id",
            new { order.Id },
            transaction);

        foreach (var pos in order.Positions)
            InsertPosition(connection, transaction, order.Id, pos);

        transaction.Commit();

        _logger.LogInformation("Order {OrderId} updated: now {PositionCount} positions, total {Total:0.00} €",
            order.Id, order.Positions.Count, order.Positions.Sum(p => p.Amount * p.UnitPrice));
    }

    public void DeleteOrder(int orderId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        connection.Execute(
            "DELETE FROM OrderPositions WHERE OrderId = @orderId",
            new { orderId },
            transaction);
        connection.Execute(
            "DELETE FROM Orders WHERE Id = @orderId",
            new { orderId },
            transaction);

        transaction.Commit();

        _logger.LogInformation("Order {OrderId} deleted", orderId);
    }

    public List<string> GetRecentNotes(int count)
    {
        using var connection = new SqliteConnection(_connectionString);

        // Newest first; a generous window, since the same text usually repeats. The distinct step runs
        // here rather than in SQL, which would treat "Käse" and "käse" as different texts.
        var notes = connection.Query<string>(
            """
            SELECT Note FROM OrderPositions
            WHERE Kind = @FreeEntry AND Note IS NOT NULL AND TRIM(Note) <> ''
            ORDER BY Id DESC
            LIMIT 500
            """,
            new { FreeEntry = (int)OrderPositionKind.FreeEntry });

        return notes.Select(n => n.Trim())
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .Take(count)
                    .ToList();
    }

    private static void InsertPosition(SqliteConnection connection, SqliteTransaction transaction, int orderId, OrderPosition pos)
    {
        connection.Execute(
            """
            INSERT INTO OrderPositions (OrderId, ProductId, Kind, Amount, UnitPriceCents, ListPriceCents, Note)
            VALUES (@OrderId, @ProductId, @Kind, @Amount, @UnitPriceCents, @ListPriceCents, @Note)
            """,
            new
            {
                OrderId = orderId,
                pos.ProductId,
                Kind = (int)pos.Kind,
                pos.Amount,
                UnitPriceCents = ToCents(pos.UnitPrice),
                ListPriceCents = pos.ListPrice is decimal listPrice ? ToCents(listPrice) : (long?)null,
                Note = string.IsNullOrWhiteSpace(pos.Note) ? null : pos.Note.Trim()
            },
            transaction);
    }

    private static long ToCents(decimal price) => (long)Math.Round(price * 100m, MidpointRounding.AwayFromZero);

    private class OrderPositionRow
    {
        public int OrderId { get; set; }
        public string OrderTime { get; set; } = "";
        public int Id { get; set; }
        public int? ProductId { get; set; }
        public int Kind { get; set; }
        public int Amount { get; set; }
        public long UnitPriceCents { get; set; }
        public long? ListPriceCents { get; set; }
        public string? Note { get; set; }
        public string ProductName { get; set; } = "";
    }
}
