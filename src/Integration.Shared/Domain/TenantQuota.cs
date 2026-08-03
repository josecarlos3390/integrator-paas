namespace Integration.Shared.Domain;

/// <summary>
/// Operational quotas per tenant to prevent a single tenant from overwhelming the system.
/// </summary>
public class TenantQuota
{
    public string TenantId { get; set; } = string.Empty;
    public int MaxEventsPerHour { get; set; } = 1000;
    public int MaxApiCallsPerMinute { get; set; } = 100;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
