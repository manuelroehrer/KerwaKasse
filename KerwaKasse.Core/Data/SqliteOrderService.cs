using System.Globalization;
using Dapper;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using Microsoft.Data.Sqlite;

namespace KerwaKasse.Core.Data;

public class SqliteOrderService : IOrderService
{
    // Canonical text format for the OrderTime column. OrderTime is stored as TEXT and filtered
    // with a lexicographic BETWEEN, so every value must use the exact same format for the date
    // range queries below to work. This SQLite-native format sorts chronologically as text;
    // an ISO round-trip format (e.g. "o", with a 'T' separator) would not align with the bounds.
    private const string SqliteDateFormat = "yyyy-MM-dd HH:mm:ss";

    private readonly string _connectionString;

    public SqliteOrderService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public void PlaceOrder(IEnumerable<OrderPosition> positions)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        var orderId = connection.ExecuteScalar<int>(
            """INSERT INTO Orders (OrderTime) VALUES (@Now); SELECT last_insert_rowid();""",
            new { Now = DateTime.Now.ToString(SqliteDateFormat) },
            transaction);

        foreach (var pos in positions)
        {
            connection.Execute(
                """
                INSERT INTO OrderPositions (OrderId, ProductId, Amount, UnitPriceCents)
                VALUES (@OrderId, @ProductId, @Amount, @UnitPriceCents)
                """,
                new
                {
                    OrderId = orderId,
                    pos.ProductId,
                    pos.Amount,
                    UnitPriceCents = ToCents(pos.UnitPrice)
                },
                transaction);
        }

        transaction.Commit();
    }

    public List<Order> GetOrdersByDate(DateTime date)
    {
        using var connection = new SqliteConnection(_connectionString);

        var dayStart = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0).ToString(SqliteDateFormat);
        var dayEnd = new DateTime(date.Year, date.Month, date.Day, 23, 59, 59).ToString(SqliteDateFormat);

        var rows = connection.Query<OrderPositionRow>(
            """
            SELECT o.Id AS OrderId, o.OrderTime, op.ProductId, op.Amount, op.UnitPriceCents,
                   COALESCE(p.Name, '(unbekanntes Produkt)') AS ProductName
            FROM Orders o
            INNER JOIN OrderPositions op ON o.Id = op.OrderId
            LEFT JOIN Products p ON p.Id = op.ProductId
            WHERE o.OrderTime BETWEEN @DayStart AND @DayEnd
            ORDER BY o.Id
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
                OrderId = row.OrderId,
                ProductId = row.ProductId,
                ProductName = row.ProductName,
                Amount = row.Amount,
                UnitPrice = row.UnitPriceCents / 100m
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
        // covers added, removed and amount-changed positions uniformly. The (OrderId, ProductId)
        // primary key means each product may appear at most once per order; the caller is expected
        // to keep products unique.
        connection.Execute(
            "DELETE FROM OrderPositions WHERE OrderId = @Id",
            new { order.Id },
            transaction);

        foreach (var pos in order.Positions)
        {
            connection.Execute(
                """
                INSERT INTO OrderPositions (OrderId, ProductId, Amount, UnitPriceCents)
                VALUES (@OrderId, @ProductId, @Amount, @UnitPriceCents)
                """,
                new
                {
                    OrderId = order.Id,
                    pos.ProductId,
                    pos.Amount,
                    UnitPriceCents = ToCents(pos.UnitPrice)
                },
                transaction);
        }

        transaction.Commit();
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
    }

    public List<SalesFigure> GetSalesFigures(DateTime from, DateTime to)
    {
        using var connection = new SqliteConnection(_connectionString);

        return connection.Query<SalesFigureRow>(
            """
            SELECT
                op.ProductId,
                COALESCE(p.Name, '(unbekanntes Produkt)') AS ProductName,
                p.Color AS ProductColor,
                SUM(op.Amount) AS TotalAmount,
                SUM(op.Amount * op.UnitPriceCents) AS TotalRevenueCents
            FROM OrderPositions op
            INNER JOIN Orders o ON o.Id = op.OrderId
            LEFT JOIN Products p ON p.Id = op.ProductId
            WHERE o.OrderTime BETWEEN @From AND @To
            GROUP BY op.ProductId
            ORDER BY TotalAmount DESC
            """,
            new { From = from.ToString(SqliteDateFormat), To = to.ToString(SqliteDateFormat) })
            .Select(r => new SalesFigure
            {
                ProductId = r.ProductId,
                ProductName = r.ProductName,
                ProductColor = r.ProductColor,
                TotalAmount = r.TotalAmount,
                TotalRevenue = r.TotalRevenueCents / 100m
            }).ToList();
    }

    private static long ToCents(decimal price) => (long)Math.Round(price * 100m, MidpointRounding.AwayFromZero);

    private class OrderPositionRow
    {
        public int OrderId { get; set; }
        public string OrderTime { get; set; } = "";
        public int ProductId { get; set; }
        public int Amount { get; set; }
        public long UnitPriceCents { get; set; }
        public string ProductName { get; set; } = "";
    }

    private class SalesFigureRow
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public string? ProductColor { get; set; }
        public int TotalAmount { get; set; }
        public long TotalRevenueCents { get; set; }
    }
}
