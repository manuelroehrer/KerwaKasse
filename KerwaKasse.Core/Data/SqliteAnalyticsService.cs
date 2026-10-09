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

    public List<SalesFigure> GetSalesFigures(DateTime from, DateTime to, IReadOnlyCollection<int>? productIds, bool includeFreeEntries = true)
    {
        using var connection = new SqliteConnection(_connectionString);

        // One group per product at its price (regular and entered prices), one per product with
        // special prices and one for the free entries without product (FigureKind = SalesFigureKind).
        var sql = $"""
            SELECT
                op.ProductId,
                CASE WHEN op.ProductId IS NULL THEN 2 WHEN op.Kind = 2 THEN 1 ELSE 0 END AS FigureKind,
                CASE WHEN op.ProductId IS NULL THEN '' ELSE COALESCE(p.Name, '(unbekanntes Produkt)') END AS ProductName,
                p.Color AS ProductColor,
                SUM(op.Amount) AS TotalAmount,
                SUM(op.Amount * op.UnitPriceCents) AS TotalRevenueCents,
                MIN(op.UnitPriceCents) AS MinUnitPriceCents,
                MAX(op.UnitPriceCents) AS MaxUnitPriceCents,
                SUM(CASE WHEN op.Kind = 1 THEN op.Amount ELSE 0 END) AS VariablePriceAmount,
                SUM(CASE WHEN op.Kind = 1 THEN op.Amount * op.UnitPriceCents ELSE 0 END) AS VariablePriceRevenueCents,
                COALESCE(MIN(CASE WHEN op.Kind = 1 THEN op.UnitPriceCents END), 0) AS VariablePriceMinCents,
                COALESCE(MAX(CASE WHEN op.Kind = 1 THEN op.UnitPriceCents END), 0) AS VariablePriceMaxCents
            FROM OrderPositions op
            INNER JOIN Orders o ON o.Id = op.OrderId
            LEFT JOIN Products p ON p.Id = op.ProductId
            WHERE o.OrderTime BETWEEN @From AND @To
            {SelectionFilter(productIds, includeFreeEntries)}
            GROUP BY op.ProductId, FigureKind
            ORDER BY TotalAmount DESC
            """;

        return connection.Query<SalesFigureRow>(sql, new
        {
            From = from.ToString(SqliteDateFormat),
            To = to.ToString(SqliteDateFormat),
            ProductIds = productIds
        }).Select(r => new SalesFigure
        {
            Kind = (SalesFigureKind)r.FigureKind,
            ProductId = r.ProductId,
            ProductName = r.ProductName,
            ProductColor = r.ProductColor,
            TotalAmount = r.TotalAmount,
            TotalRevenue = r.TotalRevenueCents / 100m,
            MinUnitPrice = r.MinUnitPriceCents / 100m,
            MaxUnitPrice = r.MaxUnitPriceCents / 100m,
            VariablePriceAmount = r.VariablePriceAmount,
            VariablePriceRevenue = r.VariablePriceRevenueCents / 100m,
            VariablePriceMin = r.VariablePriceMinCents / 100m,
            VariablePriceMax = r.VariablePriceMaxCents / 100m
        }).ToList();
    }

    public List<TimeBucketSales> GetSalesOverTime(DateTime from, DateTime to, IReadOnlyCollection<int>? productIds, AnalysisTimeResolution resolution, bool includeFreeEntries = true)
    {
        using var connection = new SqliteConnection(_connectionString);

        var sql = $"""
            SELECT o.OrderTime AS OrderTime, op.Amount AS Amount, op.UnitPriceCents AS UnitPriceCents
            FROM OrderPositions op
            INNER JOIN Orders o ON o.Id = op.OrderId
            WHERE o.OrderTime BETWEEN @From AND @To
            {SelectionFilter(productIds, includeFreeEntries)}
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

        // The product's own price: the list price (for an entered price the suggestion, for a regular
        // sale the price itself; older positions without one were sold at their unit price). Special
        // prices are not the product's price and are left out.
        var sql = $"""
            SELECT op.ProductId, COALESCE(op.ListPriceCents, op.UnitPriceCents) AS PriceCents,
                   op.Kind = 1 AS IsVariablePrice, o.OrderTime
            FROM OrderPositions op
            INNER JOIN Orders o ON o.Id = op.OrderId
            WHERE o.OrderTime BETWEEN @From AND @To
            AND op.ProductId IS NOT NULL AND op.Kind IN (0, 1)
            {SelectionFilter(productIds, includeFreeEntries: false)}
            ORDER BY op.ProductId, o.OrderTime, op.Id
            """;

        // Walk each product's sales chronologically and start a new segment whenever the price (or
        // whether it was entered) changes. A price that is used again later therefore gets its own
        // new segment instead of being merged into one misleading min–max range.
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
            var price = row.PriceCents / 100m;
            bool isVariable = row.IsVariablePrice != 0;

            if (current == null || current.ProductId != row.ProductId || current.UnitPrice != price
                || current.IsVariablePrice != isVariable)
            {
                current = new ProductPricePeriod
                {
                    ProductId = row.ProductId,
                    UnitPrice = price,
                    IsVariablePrice = isVariable,
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

    public List<FreeEntryGroup> GetFreeEntryGroups(DateTime from, DateTime to, IReadOnlyCollection<int>? productIds, bool includeFreeEntries = true)
    {
        using var connection = new SqliteConnection(_connectionString);

        // Descriptions that differ only in case are one group (the suggestions offer them that way, too).
        // Within a product the description with the most pieces comes first, with its prices together,
        // and the entries without description last.
        var sql = $"""
            SELECT op.ProductId, op.Note, op.UnitPriceCents,
                   SUM(op.Amount) AS TotalAmount, SUM(op.Amount * op.UnitPriceCents) AS TotalRevenueCents,
                   SUM(SUM(op.Amount)) OVER (PARTITION BY op.ProductId, op.Note COLLATE NOCASE) AS NoteAmount
            FROM OrderPositions op
            INNER JOIN Orders o ON o.Id = op.OrderId
            WHERE o.OrderTime BETWEEN @From AND @To
            AND op.Kind = 2
            {SelectionFilter(productIds, includeFreeEntries)}
            GROUP BY op.ProductId, op.Note COLLATE NOCASE, op.UnitPriceCents
            ORDER BY op.ProductId, op.Note IS NULL, NoteAmount DESC, op.Note COLLATE NOCASE, TotalAmount DESC, op.UnitPriceCents
            """;

        return connection.Query<FreeEntryGroupRow>(sql, new
        {
            From = from.ToString(SqliteDateFormat),
            To = to.ToString(SqliteDateFormat),
            ProductIds = productIds
        }).Select(r => new FreeEntryGroup
        {
            ProductId = r.ProductId,
            Note = r.Note,
            UnitPrice = r.UnitPriceCents / 100m,
            TotalAmount = r.TotalAmount,
            TotalRevenue = r.TotalRevenueCents / 100m
        }).ToList();
    }

    /// <summary>The WHERE condition for a selection (see <see cref="IAnalyticsService"/>): the given
    /// products, or all when <paramref name="productIds"/> is null, and the free entries without product
    /// when <paramref name="includeFreeEntries"/> is set. Uses the parameter @ProductIds.</summary>
    private static string SelectionFilter(IReadOnlyCollection<int>? productIds, bool includeFreeEntries) =>
        (productIds != null, includeFreeEntries) switch
        {
            (false, true) => "",
            (false, false) => "AND op.ProductId IS NOT NULL",
            (true, true) => "AND (op.ProductId IN @ProductIds OR op.ProductId IS NULL)",
            (true, false) => "AND op.ProductId IN @ProductIds"
        };

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
        public int? ProductId { get; set; }
        public int FigureKind { get; set; }
        public string ProductName { get; set; } = "";
        public string? ProductColor { get; set; }
        public int TotalAmount { get; set; }
        public long TotalRevenueCents { get; set; }
        public long MinUnitPriceCents { get; set; }
        public long MaxUnitPriceCents { get; set; }
        public int VariablePriceAmount { get; set; }
        public long VariablePriceRevenueCents { get; set; }
        public long VariablePriceMinCents { get; set; }
        public long VariablePriceMaxCents { get; set; }
    }

    private class FreeEntryGroupRow
    {
        public int? ProductId { get; set; }
        public string? Note { get; set; }
        public long UnitPriceCents { get; set; }
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
        public long PriceCents { get; set; }
        public long IsVariablePrice { get; set; }
        public string OrderTime { get; set; } = "";
    }
}
