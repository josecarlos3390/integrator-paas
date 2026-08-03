namespace Integration.Shared.Domain;

/// <summary>
/// State memory of the last known price by item × price list.
/// Used by PriceListPollingWorker to detect real changes in ITM1.
/// </summary>
public class PriceSnapshot
{
    public string TenantId { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public int PriceList { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal DiscountPercent { get; set; }
    public string PriceHash { get; set; } = string.Empty;
    public DateTime SapUpdateDate { get; set; }
    public int SapUpdateTs { get; set; }
    public DateTime? LastSyncedAt { get; set; }
}
