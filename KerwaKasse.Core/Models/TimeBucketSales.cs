namespace KerwaKasse.Core.Models;

/// <summary>Aggregated sales for one time bucket of the "Verlauf" (course over time) view,
/// e.g. one hour or one quarter-hour.</summary>
public class TimeBucketSales
{
    public DateTime BucketStart { get; set; }
    public int TotalAmount { get; set; }
    public decimal TotalRevenue { get; set; }
}
