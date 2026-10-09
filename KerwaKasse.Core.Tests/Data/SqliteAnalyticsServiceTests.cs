using KerwaKasse.Core.Data;
using KerwaKasse.Core.Models;
using KerwaKasse.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace KerwaKasse.Core.Tests.Data;

public class SqliteAnalyticsServiceTests : IDisposable
{
    private readonly SqliteConnection _keepAlive;
    private readonly SqliteAnalyticsService _sut;
    private readonly SqliteProductService _products;

    public SqliteAnalyticsServiceTests()
    {
        var connectionString = $"Data Source=AnalyticsTest_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();
        DatabaseInitializer.Initialize(connectionString);
        _sut = new SqliteAnalyticsService(connectionString);
        _products = new SqliteProductService(connectionString, NullLogger<SqliteProductService>.Instance);
    }

    private int AddProduct(string name, decimal price)
    {
        _products.Add(new Product { Name = name, Price = price, Available = true });
        return _products.GetAll().Last().Id;
    }

    /// <summary>Inserts an order at an exact time with the given positions, bypassing PlaceOrder
    /// (which always stamps DateTime.Now) so the time-bucket tests can control the timestamps.</summary>
    private void InsertOrder(DateTime time, params (int productId, int amount, decimal unitPrice)[] positions)
    {
        using var orderCmd = _keepAlive.CreateCommand();
        orderCmd.CommandText = "INSERT INTO Orders (OrderTime) VALUES (@t); SELECT last_insert_rowid();";
        orderCmd.Parameters.AddWithValue("@t", time.ToString("yyyy-MM-dd HH:mm:ss"));
        long orderId = (long)orderCmd.ExecuteScalar()!;

        foreach (var (productId, amount, unitPrice) in positions)
        {
            using var posCmd = _keepAlive.CreateCommand();
            posCmd.CommandText =
                "INSERT INTO OrderPositions (OrderId, ProductId, Amount, UnitPriceCents) VALUES (@o, @p, @a, @c)";
            posCmd.Parameters.AddWithValue("@o", orderId);
            posCmd.Parameters.AddWithValue("@p", productId);
            posCmd.Parameters.AddWithValue("@a", amount);
            posCmd.Parameters.AddWithValue("@c", (long)Math.Round(unitPrice * 100m));
            posCmd.ExecuteNonQuery();
        }
    }

    /// <summary>Inserts a single position of any kind (variable price, special price, free entry) in its
    /// own order at an exact time.</summary>
    private void InsertEntry(DateTime time, int? productId, OrderPositionKind kind, int amount, decimal unitPrice,
        decimal? listPrice = null, string? note = null)
    {
        using var orderCmd = _keepAlive.CreateCommand();
        orderCmd.CommandText = "INSERT INTO Orders (OrderTime) VALUES (@t); SELECT last_insert_rowid();";
        orderCmd.Parameters.AddWithValue("@t", time.ToString("yyyy-MM-dd HH:mm:ss"));
        long orderId = (long)orderCmd.ExecuteScalar()!;

        using var posCmd = _keepAlive.CreateCommand();
        posCmd.CommandText = "INSERT INTO OrderPositions (OrderId, ProductId, Kind, Amount, UnitPriceCents, ListPriceCents, Note) " +
                             "VALUES (@o, @p, @k, @a, @c, @l, @n)";
        posCmd.Parameters.AddWithValue("@o", orderId);
        posCmd.Parameters.AddWithValue("@p", (object?)productId ?? DBNull.Value);
        posCmd.Parameters.AddWithValue("@k", (int)kind);
        posCmd.Parameters.AddWithValue("@a", amount);
        posCmd.Parameters.AddWithValue("@c", (long)Math.Round(unitPrice * 100m));
        posCmd.Parameters.AddWithValue("@l", listPrice is decimal l ? (long)Math.Round(l * 100m) : DBNull.Value);
        posCmd.Parameters.AddWithValue("@n", (object?)note ?? DBNull.Value);
        posCmd.ExecuteNonQuery();
    }

    [Fact]
    public void GetSalesFigures_NoFilter_AggregatesAllProducts()
    {
        int p1 = AddProduct("Bratwurst", 3.00m);
        int p2 = AddProduct("Bier", 3.50m);
        InsertOrder(DateTime.Today.AddHours(10), (p1, 5, 3.00m), (p2, 2, 3.50m));
        InsertOrder(DateTime.Today.AddHours(11), (p1, 3, 3.00m));

        var figures = _sut.GetSalesFigures(DateTime.Today, DateTime.Today.AddDays(1), null);

        Assert.Equal(2, figures.Count);
        var bratwurst = figures.Single(f => f.ProductId == p1);
        Assert.Equal(8, bratwurst.TotalAmount);
        Assert.Equal(24.00m, bratwurst.TotalRevenue);
    }

    [Fact]
    public void GetSalesFigures_WithProductFilter_ReturnsOnlySelected()
    {
        int p1 = AddProduct("Bratwurst", 3.00m);
        int p2 = AddProduct("Bier", 3.50m);
        InsertOrder(DateTime.Today.AddHours(10), (p1, 5, 3.00m), (p2, 2, 3.50m));

        var figures = _sut.GetSalesFigures(DateTime.Today, DateTime.Today.AddDays(1), new[] { p1 });

        var only = Assert.Single(figures);
        Assert.Equal(p1, only.ProductId);
        Assert.Equal(5, only.TotalAmount);
    }

    [Fact]
    public void GetSalesFigures_EmptyFilter_SelectsNoProduct()
    {
        int p1 = AddProduct("Bratwurst", 3.00m);
        int p2 = AddProduct("Bier", 3.50m);
        InsertOrder(DateTime.Today.AddHours(10), (p1, 1, 3.00m), (p2, 1, 3.50m));
        InsertEntry(DateTime.Today.AddHours(11), null, OrderPositionKind.FreeEntry, 1, 4.00m, note: "Kuchen");

        // No product, only the free entries (e.g. just "Freie Eingabe" ticked in the filter).
        var figures = _sut.GetSalesFigures(DateTime.Today, DateTime.Today.AddDays(1), Array.Empty<int>());

        var only = Assert.Single(figures);
        Assert.Equal(SalesFigureKind.FreeEntries, only.Kind);
    }

    [Fact]
    public void GetSalesFigures_SeparatesSpecialPricesAndFreeEntries()
    {
        int bratwurst = AddProduct("Bratwurst", 3.50m);
        int mackerel = AddProduct("Makrele", 7.00m);
        var t = DateTime.Today.AddHours(10);
        InsertOrder(t, (bratwurst, 2, 3.50m));
        InsertEntry(t, bratwurst, OrderPositionKind.FreeEntry, 1, 2.00m, 3.50m, "Helfer");
        InsertEntry(t, mackerel, OrderPositionKind.VariablePrice, 1, 8.50m, 7.00m);
        InsertEntry(t, mackerel, OrderPositionKind.Regular, 1, 7.00m, 7.00m);
        InsertEntry(t, null, OrderPositionKind.FreeEntry, 1, 4.00m, note: "Kuchen");
        InsertEntry(t, null, OrderPositionKind.FreeEntry, 2, 1.00m);

        var figures = _sut.GetSalesFigures(DateTime.Today, DateTime.Today.AddDays(1), null);

        Assert.Equal(4, figures.Count);
        var bratwurstRow = figures.Single(f => f.ProductId == bratwurst && f.Kind == SalesFigureKind.Product);
        Assert.Equal(2, bratwurstRow.TotalAmount);
        var special = figures.Single(f => f.Kind == SalesFigureKind.SpecialPrice);
        Assert.Equal(bratwurst, special.ProductId);
        Assert.Equal("Bratwurst", special.ProductName);
        Assert.Equal(2.00m, special.TotalRevenue);
        // Entered and regular prices of the mackerel are one row.
        var mackerelRow = figures.Single(f => f.ProductId == mackerel);
        Assert.Equal(SalesFigureKind.Product, mackerelRow.Kind);
        Assert.Equal(2, mackerelRow.TotalAmount);
        var free = figures.Single(f => f.Kind == SalesFigureKind.FreeEntries);
        Assert.Null(free.ProductId);
        Assert.Equal(string.Empty, free.ProductName);
        Assert.Equal(3, free.TotalAmount);
        Assert.Equal(6.00m, free.TotalRevenue);
    }

    [Fact]
    public void GetSalesFigures_ReportsTheEnteredPrices()
    {
        int mackerel = AddProduct("Makrele", 7.00m);
        var t = DateTime.Today.AddHours(10);
        InsertEntry(t, mackerel, OrderPositionKind.VariablePrice, 2, 8.50m, 7.00m);
        InsertEntry(t, mackerel, OrderPositionKind.VariablePrice, 1, 6.00m, 7.00m);
        InsertEntry(t, mackerel, OrderPositionKind.Regular, 1, 7.00m, 7.00m);

        var row = Assert.Single(_sut.GetSalesFigures(DateTime.Today, DateTime.Today.AddDays(1), null));

        Assert.Equal(3, row.VariablePriceAmount);
        Assert.Equal(23.00m, row.VariablePriceRevenue);
        Assert.Equal(6.00m, row.VariablePriceMin);
        Assert.Equal(8.50m, row.VariablePriceMax);
    }

    [Fact]
    public void GetSalesFigures_FreeEntriesFollowTheSelection()
    {
        int bratwurst = AddProduct("Bratwurst", 3.50m);
        int beer = AddProduct("Bier", 3.00m);
        var t = DateTime.Today.AddHours(10);
        InsertOrder(t, (bratwurst, 1, 3.50m), (beer, 1, 3.00m));
        InsertEntry(t, bratwurst, OrderPositionKind.FreeEntry, 1, 2.00m, 3.50m);
        InsertEntry(t, null, OrderPositionKind.FreeEntry, 1, 4.00m, note: "Kuchen");
        var (from, to) = (DateTime.Today, DateTime.Today.AddDays(1));

        var allWithoutFree = _sut.GetSalesFigures(from, to, null, includeFreeEntries: false);
        var bratwurstWithFree = _sut.GetSalesFigures(from, to, new[] { bratwurst }, includeFreeEntries: true);
        var bratwurstOnly = _sut.GetSalesFigures(from, to, new[] { bratwurst }, includeFreeEntries: false);

        Assert.Equal(3, allWithoutFree.Count);
        Assert.DoesNotContain(allWithoutFree, f => f.Kind == SalesFigureKind.FreeEntries);
        // A product brings its special prices along.
        Assert.Equal(new[] { SalesFigureKind.Product, SalesFigureKind.SpecialPrice, SalesFigureKind.FreeEntries },
            bratwurstWithFree.Select(f => f.Kind).OrderBy(k => k));
        Assert.Equal(new[] { SalesFigureKind.Product, SalesFigureKind.SpecialPrice },
            bratwurstOnly.Select(f => f.Kind).OrderBy(k => k));
    }

    [Fact]
    public void GetSalesFigures_ReportsLowestAndHighestUnitPrice()
    {
        int p1 = AddProduct("Bratwurst", 3.50m);
        int p2 = AddProduct("Bier", 3.50m);
        InsertOrder(DateTime.Today.AddHours(10), (p1, 2, 3.00m), (p2, 1, 3.50m));
        InsertOrder(DateTime.Today.AddHours(11), (p1, 1, 3.50m), (p2, 4, 3.50m));

        var figures = _sut.GetSalesFigures(DateTime.Today, DateTime.Today.AddDays(1), null);

        var bratwurst = figures.Single(f => f.ProductId == p1);
        Assert.Equal(3.00m, bratwurst.MinUnitPrice);
        Assert.Equal(3.50m, bratwurst.MaxUnitPrice);
        var bier = figures.Single(f => f.ProductId == p2);
        Assert.Equal(3.50m, bier.MinUnitPrice);
        Assert.Equal(3.50m, bier.MaxUnitPrice);
    }

    [Fact]
    public void GetPricePeriods_GroupsByProductAndPriceWithFirstAndLastSale()
    {
        int p1 = AddProduct("Bratwurst", 3.50m);
        var year1 = new DateTime(2022, 7, 24, 18, 0, 0);
        var year1End = new DateTime(2022, 7, 29, 21, 0, 0);
        var year2 = new DateTime(2023, 7, 25, 19, 0, 0);
        InsertOrder(year1, (p1, 2, 3.00m));
        InsertOrder(year1End, (p1, 1, 3.00m));
        InsertOrder(year2, (p1, 4, 3.50m));

        var periods = _sut.GetPricePeriods(new DateTime(2022, 1, 1), new DateTime(2024, 1, 1), null);

        Assert.Equal(2, periods.Count);
        Assert.Equal(3.00m, periods[0].UnitPrice); // ordered by first sale
        Assert.Equal(year1, periods[0].From);
        Assert.Equal(year1End, periods[0].To);
        Assert.Equal(3.50m, periods[1].UnitPrice);
        Assert.Equal(year2, periods[1].From);
        Assert.Equal(year2, periods[1].To);
    }

    [Fact]
    public void GetPricePeriods_PriceUsedAgainLater_GetsItsOwnSegment()
    {
        int p1 = AddProduct("Bratwurst", 3.00m);
        InsertOrder(new DateTime(2022, 7, 24, 18, 0, 0), (p1, 1, 3.00m));
        InsertOrder(new DateTime(2023, 7, 25, 18, 0, 0), (p1, 1, 3.50m));
        InsertOrder(new DateTime(2024, 7, 26, 18, 0, 0), (p1, 1, 3.00m)); // back to the old price

        var periods = _sut.GetPricePeriods(new DateTime(2022, 1, 1), new DateTime(2025, 1, 1), null);

        Assert.Equal(3, periods.Count);
        Assert.Equal(new[] { 3.00m, 3.50m, 3.00m }, periods.Select(p => p.UnitPrice));
        Assert.Equal(new DateTime(2024, 7, 26, 18, 0, 0), periods[2].From);
    }

    [Fact]
    public void GetPricePeriods_TakesTheSuggestionOfEnteredPricesAndLeavesOutSpecialPrices()
    {
        int mackerel = AddProduct("Makrele", 7.00m);
        var day = new DateTime(2026, 8, 1, 12, 0, 0);
        InsertEntry(day, mackerel, OrderPositionKind.VariablePrice, 1, 8.50m, 7.00m);
        InsertEntry(day.AddHours(1), mackerel, OrderPositionKind.VariablePrice, 1, 6.50m, 7.00m);
        InsertEntry(day.AddHours(2), mackerel, OrderPositionKind.FreeEntry, 1, 3.00m, 7.00m, "Helfer");
        // Later sold at a fixed price.
        InsertEntry(day.AddDays(1), mackerel, OrderPositionKind.Regular, 1, 7.50m, 7.50m);

        var periods = _sut.GetPricePeriods(day.Date, day.AddDays(2), null);

        Assert.Equal(2, periods.Count);
        Assert.Equal(7.00m, periods[0].UnitPrice);
        Assert.True(periods[0].IsVariablePrice);
        Assert.Equal(day.AddHours(1), periods[0].To);
        Assert.Equal(7.50m, periods[1].UnitPrice);
        Assert.False(periods[1].IsVariablePrice);
    }

    [Fact]
    public void GetFreeEntryGroups_GroupsByProductDescriptionAndPrice()
    {
        int bratwurst = AddProduct("Bratwurst", 3.50m);
        var t = DateTime.Today.AddHours(10);
        InsertEntry(t, null, OrderPositionKind.FreeEntry, 1, 4.00m, note: "Kuchen");
        InsertEntry(t, null, OrderPositionKind.FreeEntry, 2, 4.00m, note: "kuchen");
        InsertEntry(t, null, OrderPositionKind.FreeEntry, 1, 5.00m, note: "Kuchen");
        InsertEntry(t, null, OrderPositionKind.FreeEntry, 1, 2.00m);
        InsertEntry(t, bratwurst, OrderPositionKind.FreeEntry, 1, 2.00m, 3.50m, "Helfer");
        InsertEntry(t, bratwurst, OrderPositionKind.VariablePrice, 1, 2.00m, 3.50m); // not a free entry

        var groups = _sut.GetFreeEntryGroups(DateTime.Today, DateTime.Today.AddDays(1), null);

        Assert.Equal(4, groups.Count);
        var withoutProduct = groups.Where(g => g.ProductId == null).ToList();
        // Same description in another case is the same group; a description's prices stay together,
        // the largest first, and entries without description come last.
        Assert.Equal(3, withoutProduct[0].TotalAmount);
        Assert.Equal(4.00m, withoutProduct[0].UnitPrice);
        Assert.Equal(12.00m, withoutProduct[0].TotalRevenue);
        Assert.Equal(5.00m, withoutProduct[1].UnitPrice);
        Assert.Null(withoutProduct[2].Note);
        var special = Assert.Single(groups, g => g.ProductId == bratwurst);
        Assert.Equal("Helfer", special.Note);
    }

    [Fact]
    public void GetFreeEntryGroups_FollowsTheSelection()
    {
        int bratwurst = AddProduct("Bratwurst", 3.50m);
        int beer = AddProduct("Bier", 3.00m);
        var t = DateTime.Today.AddHours(10);
        InsertEntry(t, bratwurst, OrderPositionKind.FreeEntry, 1, 2.00m, 3.50m);
        InsertEntry(t, beer, OrderPositionKind.FreeEntry, 1, 2.00m, 3.00m);
        InsertEntry(t, null, OrderPositionKind.FreeEntry, 1, 4.00m);

        var groups = _sut.GetFreeEntryGroups(DateTime.Today, DateTime.Today.AddDays(1), new[] { beer }, includeFreeEntries: false);

        Assert.Equal(beer, Assert.Single(groups).ProductId);
    }

    [Fact]
    public void GetPricePeriods_WithProductFilter_ReturnsOnlySelected()
    {
        int p1 = AddProduct("Bratwurst", 3.00m);
        int p2 = AddProduct("Bier", 3.50m);
        InsertOrder(DateTime.Today.AddHours(10), (p1, 1, 3.00m), (p2, 1, 3.50m));

        var periods = _sut.GetPricePeriods(DateTime.Today, DateTime.Today.AddDays(1), new[] { p2 });

        var only = Assert.Single(periods);
        Assert.Equal(p2, only.ProductId);
        Assert.Equal(3.50m, only.UnitPrice);
    }

    [Fact]
    public void GetSalesFigures_RespectsDateRange()
    {
        int p1 = AddProduct("Bratwurst", 3.00m);
        InsertOrder(DateTime.Today.AddHours(10), (p1, 2, 3.00m));
        InsertOrder(DateTime.Today.AddDays(-3), (p1, 9, 3.00m));

        var figures = _sut.GetSalesFigures(DateTime.Today, DateTime.Today.AddDays(1), null);

        var row = Assert.Single(figures);
        Assert.Equal(2, row.TotalAmount);
    }

    [Fact]
    public void GetSalesOverTime_Hourly_BucketsAndFillsGaps()
    {
        int p1 = AddProduct("Bratwurst", 2.00m);
        InsertOrder(DateTime.Today.AddHours(10).AddMinutes(15), (p1, 3, 2.00m));
        InsertOrder(DateTime.Today.AddHours(10).AddMinutes(45), (p1, 2, 2.00m));
        InsertOrder(DateTime.Today.AddHours(12).AddMinutes(5), (p1, 4, 2.00m));

        var buckets = _sut.GetSalesOverTime(DateTime.Today, DateTime.Today.AddDays(1), null, AnalysisTimeResolution.Hour);

        // 10:00 (5), 11:00 (0, gap-filled), 12:00 (4)
        Assert.Equal(3, buckets.Count);
        Assert.Equal(DateTime.Today.AddHours(10), buckets[0].BucketStart);
        Assert.Equal(5, buckets[0].TotalAmount);
        Assert.Equal(0, buckets[1].TotalAmount);
        Assert.Equal(DateTime.Today.AddHours(11), buckets[1].BucketStart);
        Assert.Equal(4, buckets[2].TotalAmount);
    }

    [Fact]
    public void GetSalesOverTime_QuarterHour_SeparatesWithinTheHour()
    {
        int p1 = AddProduct("Bratwurst", 2.00m);
        InsertOrder(DateTime.Today.AddHours(10).AddMinutes(5), (p1, 1, 2.00m));
        InsertOrder(DateTime.Today.AddHours(10).AddMinutes(20), (p1, 1, 2.00m));

        var buckets = _sut.GetSalesOverTime(DateTime.Today, DateTime.Today.AddDays(1), null, AnalysisTimeResolution.QuarterHour);

        Assert.Equal(2, buckets.Count);
        Assert.Equal(DateTime.Today.AddHours(10), buckets[0].BucketStart);
        Assert.Equal(DateTime.Today.AddHours(10).AddMinutes(15), buckets[1].BucketStart);
    }

    [Fact]
    public void GetSalesOverTime_Daily_BucketsPerDayAndFillsGaps()
    {
        int p1 = AddProduct("Bratwurst", 2.00m);
        InsertOrder(DateTime.Today.AddHours(10), (p1, 5, 2.00m));
        InsertOrder(DateTime.Today.AddHours(14), (p1, 3, 2.00m));           // same day → same bucket
        InsertOrder(DateTime.Today.AddDays(2).AddHours(9), (p1, 4, 2.00m)); // two days later

        var buckets = _sut.GetSalesOverTime(DateTime.Today, DateTime.Today.AddDays(3), null, AnalysisTimeResolution.Day);

        Assert.Equal(3, buckets.Count); // today, the empty day in between (gap-filled), and day+2
        Assert.Equal(DateTime.Today, buckets[0].BucketStart);
        Assert.Equal(8, buckets[0].TotalAmount);
        Assert.Equal(0, buckets[1].TotalAmount);
        Assert.Equal(DateTime.Today.AddDays(2), buckets[2].BucketStart);
        Assert.Equal(4, buckets[2].TotalAmount);
    }

    [Fact]
    public void GetSalesOverTime_FiltersByProducts()
    {
        int p1 = AddProduct("Bratwurst", 2.00m);
        int p2 = AddProduct("Bier", 3.00m);
        InsertOrder(DateTime.Today.AddHours(10), (p1, 5, 2.00m), (p2, 9, 3.00m));

        var buckets = _sut.GetSalesOverTime(DateTime.Today, DateTime.Today.AddDays(1), new[] { p1 }, AnalysisTimeResolution.Hour);

        var bucket = Assert.Single(buckets);
        Assert.Equal(5, bucket.TotalAmount);
    }

    [Fact]
    public void GetSalesOverTime_WithoutFreeEntries_LeavesThemOut()
    {
        int p1 = AddProduct("Bratwurst", 2.00m);
        InsertOrder(DateTime.Today.AddHours(10), (p1, 5, 2.00m));
        InsertEntry(DateTime.Today.AddHours(10), null, OrderPositionKind.FreeEntry, 3, 4.00m);

        var with = _sut.GetSalesOverTime(DateTime.Today, DateTime.Today.AddDays(1), null, AnalysisTimeResolution.Hour);
        var without = _sut.GetSalesOverTime(DateTime.Today, DateTime.Today.AddDays(1), null, AnalysisTimeResolution.Hour, includeFreeEntries: false);

        Assert.Equal(8, Assert.Single(with).TotalAmount);
        Assert.Equal(5, Assert.Single(without).TotalAmount);
    }

    [Fact]
    public void GetSalesOverTime_NoData_ReturnsEmpty()
    {
        var buckets = _sut.GetSalesOverTime(DateTime.Today, DateTime.Today.AddDays(1), null, AnalysisTimeResolution.Hour);
        Assert.Empty(buckets);
    }

    public void Dispose()
    {
        _keepAlive.Dispose();
    }
}
