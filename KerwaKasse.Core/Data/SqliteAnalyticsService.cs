using System.Globalization;
using Dapper;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using Microsoft.Data.Sqlite;

namespace KerwaKasse.Core.Data;

public class SqliteAnalyticsService : IAnalyticsService
{
    // Same canonical text format as SqliteOrderService: OrderTime is stored as TEXT and filtered with
    // a lexicographic BETWEEN, so the bounds must use this exact format. See SqliteOrderService.
    private const string SqliteDateFormat = "yyyy-MM-dd HH:mm:ss";

    private readonly string _connectionString;

    public SqliteAnalyticsService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public List<SalesFigure> GetSalesFigures(DateTime from, DateTime to, IReadOnlyCollection<int>? productIds)
    {
        using var connection = new SqliteConnection(_connectionString);

        bool filtered = productIds is { Count: > 0 };

        var sql = $"""
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
            {(filtered ? "AND op.ProductId IN @ProductIds" : "")}
            GROUP BY op.ProductId
            ORDER BY TotalAmount DESC
            """;

        return connection.Query<SalesFigureRow>(sql, new
        {
            From = from.ToString(SqliteDateFormat),
            To = to.ToString(SqliteDateFormat),
            ProductIds = productIds
        }).Select(r => new SalesFigure
        {
            ProductId = r.ProductId,
            ProductName = r.ProductName,
            ProductColor = r.ProductColor,
            TotalAmount = r.TotalAmount,
            TotalRevenue = r.TotalRevenueCents / 100m
        }).ToList();
    }

    public List<TimeBucketSales> GetSalesOverTime(DateTime from, DateTime to, IReadOnlyCollection<int>? productIds, AnalysisTimeResolution resolution)
    {
        using var connection = new SqliteConnection(_connectionString);

        bool filtered = productIds is { Count: > 0 };

        var sql = $"""
            SELECT o.OrderTime AS OrderTime, op.Amount AS Amount, op.UnitPriceCents AS UnitPriceCents
            FROM OrderPositions op
            INNER JOIN Orders o ON o.Id = op.OrderId
            WHERE o.OrderTime BETWEEN @From AND @To
            {(filtered ? "AND op.ProductId IN @ProductIds" : "")}
            """;

        var rows = connection.Query<PositionTimeRow>(sql, new
        {
            From = from.ToString(SqliteDateFormat),
            To = to.ToString(SqliteDateFormat),
            ProductIds = productIds
        }).ToList();

        int bucketMinutes = resolution switch
        {
            AnalysisTimeResolution.QuarterHour => 15,
            AnalysisTimeResolution.Day => 1440,
            _ => 60
        };

        var buckets = new SortedDictionary<DateTime, TimeBucketSales>();
        foreach (var row in rows)
        {
            var time = DateTime.ParseExact(row.OrderTime, SqliteDateFormat, CultureInfo.InvariantCulture);
            var bucketStart = FloorToBucket(time, bucketMinutes);

            if (!buckets.TryGetValue(bucketStart, out var bucket))
            {
                bucket = new TimeBucketSales { BucketStart = bucketStart };
                buckets[bucketStart] = bucket;
            }

            bucket.TotalAmount += row.Amount;
            bucket.TotalRevenue += row.Amount * row.UnitPriceCents / 100m;
        }

        if (buckets.Count == 0) return new List<TimeBucketSales>();

        // Fill the gaps between the first and last populated bucket with zeros, so the column chart
        // shows a continuous timeline instead of collapsing quiet periods.
        var result = new List<TimeBucketSales>();
        var step = TimeSpan.FromMinutes(bucketMinutes);
        for (var t = buckets.Keys.First(); t <= buckets.Keys.Last(); t = t.Add(step))
        {
            result.Add(buckets.TryGetValue(t, out var b) ? b : new TimeBucketSales { BucketStart = t });
        }

        return result;
    }

    public List<ProductPricePeriod> GetPricePeriods(DateTime from, DateTime to, IReadOnlyCollection<int>? productIds)
    {
        using var connection = new SqliteConnection(_connectionString);

        bool filtered = productIds is { Count: > 0 };

        var sql = $"""
            SELECT op.ProductId, op.UnitPriceCents, o.OrderTime
            FROM OrderPositions op
            INNER JOIN Orders o ON o.Id = op.OrderId
            WHERE o.OrderTime BETWEEN @From AND @To
            {(filtered ? "AND op.ProductId IN @ProductIds" : "")}
            ORDER BY op.ProductId, o.OrderTime
            """;

        // Walk each product's sales chronologically and start a new segment whenever the unit
        // price changes. A price that is used again later therefore gets its own new segment
        // instead of being merged into one misleading min–max range.
        var result = new List<ProductPricePeriod>();
        ProductPricePeriod? current = null;
        foreach (var row in connection.Query<PricePeriodRow>(sql, new
        {
            From = from.ToString(SqliteDateFormat),
            To = to.ToString(SqliteDateFormat),
            ProductIds = productIds
        }))
        {
            var time = DateTime.ParseExact(row.OrderTime, SqliteDateFormat, CultureInfo.InvariantCulture);
            var unitPrice = row.UnitPriceCents / 100m;

            if (current == null || current.ProductId != row.ProductId || current.UnitPrice != unitPrice)
            {
                current = new ProductPricePeriod
                {
                    ProductId = row.ProductId,
                    UnitPrice = unitPrice,
                    From = time,
                    To = time
                };
                result.Add(current);
            }
            else
            {
                current.To = time;
            }
        }

        return result;
    }

    /// <summary>Rounds a timestamp down to the start of its bucket within the day (buckets are aligned
    /// to midnight, so e.g. 15-minute buckets always start at :00/:15/:30/:45).</summary>
    private static DateTime FloorToBucket(DateTime time, int bucketMinutes)
    {
        var dayStart = time.Date;
        var minutesSinceDayStart = (int)(time - dayStart).TotalMinutes;
        var flooredMinutes = minutesSinceDayStart / bucketMinutes * bucketMinutes;
        return dayStart.AddMinutes(flooredMinutes);
    }

    private class SalesFigureRow
    {
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public string? ProductColor { get; set; }
        public int TotalAmount { get; set; }
        public long TotalRevenueCents { get; set; }
    }

    private class PositionTimeRow
    {
        public string OrderTime { get; set; } = "";
        public int Amount { get; set; }
        public long UnitPriceCents { get; set; }
    }

    private class PricePeriodRow
    {
        public int ProductId { get; set; }
        public long UnitPriceCents { get; set; }
        public string OrderTime { get; set; } = "";
    }
}
