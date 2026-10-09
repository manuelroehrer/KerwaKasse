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
/// OrderPositions but never mutates anything.
/// <para>The selection is the same everywhere: <c>productIds</c> null means all products, otherwise
/// only the given ones (an empty list: none), each with its special prices. The free entries without a
/// product belong to no product and come along when <c>includeFreeEntries</c> is set.</para></summary>
public interface IAnalyticsService
{
    /// <summary>Totals within the range: one figure per product, one per product with special prices
    /// and one for all free entries without product (see <see cref="SalesFigureKind"/>). Ordered by
    /// amount descending.</summary>
    List<SalesFigure> GetSalesFigures(DateTime from, DateTime to, IReadOnlyCollection<int>? productIds, bool includeFreeEntries = true);

    /// <summary>Totals per time bucket within the range for the given resolution. Empty buckets
    /// between the first and last sale are filled with zero so the column chart has no gaps.</summary>
    List<TimeBucketSales> GetSalesOverTime(DateTime from, DateTime to, IReadOnlyCollection<int>? productIds, AnalysisTimeResolution resolution, bool includeFreeEntries = true);

    /// <summary>The price segments per product within the range: one entry per consecutive stretch of
    /// sales at the same price, with its first and last sale time. A price used again after an
    /// interruption yields a new segment. Only the product's own price counts: the suggested one for
    /// sales at an entered price, no special prices. Ordered by product, then chronologically.</summary>
    List<ProductPricePeriod> GetPricePeriods(DateTime from, DateTime to, IReadOnlyCollection<int>? productIds);

    /// <summary>The free entries within the range (special prices and those without product), grouped
    /// by product, description and price. Within a product the description with the most pieces comes
    /// first, its prices together, and the entries without description last.</summary>
    List<FreeEntryGroup> GetFreeEntryGroups(DateTime from, DateTime to, IReadOnlyCollection<int>? productIds, bool includeFreeEntries = true);
}
