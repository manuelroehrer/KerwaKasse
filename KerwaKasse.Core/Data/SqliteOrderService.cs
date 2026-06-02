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
            """INSERT INTO "Order" (OrderTime) VALUES (@Now); SELECT last_insert_rowid();""",
            new { Now = DateTime.Now.ToString(SqliteDateFormat) },
            transaction);

        foreach (var pos in positions)
        {
            connection.Execute(
                """
                INSERT INTO OrderPosition (OrderId, ProductId, Amount, UnitPrice, ProductDescription)
                VALUES (@OrderId, @ProductId, @Amount, @UnitPrice, @ProductDescription)
                """,
                new
                {
                    OrderId = orderId,
                    pos.ProductId,
                    pos.Amount,
                    pos.UnitPrice,
                    pos.ProductDescription
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
            SELECT o.Id AS OrderId, o.OrderTime, op.ProductId, op.Amount, op.UnitPrice, op.ProductDescription
            FROM "Order" o
            INNER JOIN OrderPosition op ON o.Id = op.OrderId
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
                ProductDescription = row.ProductDescription,
                Amount = row.Amount,
                UnitPrice = row.UnitPrice
            });
        }

        return orders.Values.ToList();
    }

    public void UpdateOrderPositions(Order order)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var transaction = connection.BeginTransaction();

        foreach (var pos in order.Positions)
        {
            connection.Execute(
                """
                UPDATE OrderPosition SET Amount = @Amount
                WHERE OrderId = @OrderId AND ProductId = @ProductId
                """,
                new { pos.Amount, pos.OrderId, pos.ProductId },
                transaction);
        }

        transaction.Commit();
    }

    public List<SalesFigure> GetSalesFigures(DateTime from, DateTime to)
    {
        using var connection = new SqliteConnection(_connectionString);

        return connection.Query<SalesFigure>(
            """
            SELECT
                op.ProductId,
                op.ProductDescription AS ProductDescription,
                p.Color AS ProductColor,
                SUM(op.Amount) AS TotalAmount,
                SUM(op.Amount * op.UnitPrice) AS TotalRevenue
            FROM OrderPosition op
            INNER JOIN "Order" o ON o.Id = op.OrderId
            LEFT JOIN Product p ON p.Id = op.ProductId
            WHERE o.OrderTime BETWEEN @From AND @To
            GROUP BY op.ProductId, op.ProductDescription
            ORDER BY TotalAmount DESC
            """,
            new { From = from.ToString(SqliteDateFormat), To = to.ToString(SqliteDateFormat) }).ToList();
    }

    private class OrderPositionRow
    {
        public int OrderId { get; set; }
        public string OrderTime { get; set; } = "";
        public int ProductId { get; set; }
        public int Amount { get; set; }
        public decimal UnitPrice { get; set; }
        public string ProductDescription { get; set; } = "";
    }
}
