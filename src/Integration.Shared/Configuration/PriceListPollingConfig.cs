namespace Integration.Shared.Configuration;

public class PriceListPollingConfig
{
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 10;
    private int _batchSize = 500;
    public int BatchSize
    {
        get => _batchSize;
        set => _batchSize = value >= 1 ? value : 1;
    }
    public int InitialLookbackDays { get; set; } = 30;
    public int MaxPayloadItems { get; set; } = 100;

    /// <summary>
    /// Event grouping: "ListNum" (by price list) or "CardCode" (by customer).
    /// </summary>
    public string GroupBy { get; set; } = "ListNum";

    /// <summary>
    /// If true, also scans ITM1 (list prices) on each cycle.
    /// ITM1 has no dates, so it is always a full scan.
    /// </summary>
    public bool IncludeItemPrices { get; set; } = false;

    /// <summary>
    /// Interval in hours for ITM1 full sync (only if IncludeItemPrices = true).
    /// </summary>
    public int ItemPriceFullSyncIntervalHours { get; set; } = 24;
}
