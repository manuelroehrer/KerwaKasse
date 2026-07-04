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
    public void GetSalesFigures_EmptyFilter_TreatedAsAllProducts()
    {
        int p1 = AddProduct("Bratwurst", 3.00m);
        int p2 = AddProduct("Bier", 3.50m);
        InsertOrder(DateTime.Today.AddHours(10), (p1, 1, 3.00m), (p2, 1, 3.50m));

        var figures = _sut.GetSalesFigures(DateTime.Today, DateTime.Today.AddDays(1), Array.Empty<int>());

        Assert.Equal(2, figures.Count);
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
