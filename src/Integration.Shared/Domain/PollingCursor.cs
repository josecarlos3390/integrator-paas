namespace Integration.Shared.Domain;

/// <summary>
/// Marks how far the last polling cycle got.
/// Allows resuming without reprocessing from the beginning.
/// </summary>
public class PollingCursor
{
    public string TenantId { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public DateTime LastUpdateDate { get; set; }
    public int LastUpdateTs { get; set; }
    public DateTime LastRunAt { get; set; }
}
