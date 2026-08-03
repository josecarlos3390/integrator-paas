namespace Integration.Shared.Domain;

/// <summary>
/// Tenant-configurable feature flag. Allows enabling/disabling
/// integration flows without needing to redeploy.
/// </summary>
public class TenantFeatureFlag
{
    public string TenantId { get; set; } = string.Empty;
    public string FeatureKey { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public DateTime UpdatedAt { get; set; }
}
