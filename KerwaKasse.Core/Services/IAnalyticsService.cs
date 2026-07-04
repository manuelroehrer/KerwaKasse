using KerwaKasse.Core.Models;

namespace KerwaKasse.Core.Services;

/// <summary>Resolution of the time-course aggregation.</summary>
public enum AnalysisTimeResolution
{
    Day,
    Hour,
    QuarterHour
}

/// <summary>Read-only sales aggregates for the analysis view. Deliberately separate from
/// <see cref="IOrderService"/> (the write path): every method aggregates over the same
/// OrderPositions but never mutates anything.</summary>
public interface IAnalyticsService
{
    /// <summary>Per-product totals within the range. When <paramref name="productIds"/> is null or
    /// empty, all products are included; otherwise only the given ones. Ordered by amount descending.</summary>
    List<SalesFigure> GetSalesFigures(DateTime from, DateTime to, IReadOnlyCollection<int>? productIds);

    /// <summary>Totals per time bucket within the range for the given resolution. Empty buckets
    /// between the first and last sale are filled with zero so the column chart has no gaps.</summary>
    List<TimeBucketSales> GetSalesOverTime(DateTime from, DateTime to, IReadOnlyCollection<int>? productIds, AnalysisTimeResolution resolution);

    /// <summary>The unit-price segments per product within the range: one entry per consecutive
    /// stretch of sales at the same price, with its first and last sale time. A price used again
    /// after an interruption yields a new segment. Same product filter semantics as
    /// <see cref="GetSalesFigures"/>; ordered by product, then chronologically.</summary>
    List<ProductPricePeriod> GetPricePeriods(DateTime from, DateTime to, IReadOnlyCollection<int>? productIds);
}
