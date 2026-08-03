namespace Integration.Shared.Services;

/// <summary>
/// Tenant feature flag service with in-memory cache.
/// </summary>
public interface ITenantFeatureService
{
    /// <summary>
    /// Determines if a feature is enabled for a tenant.
    /// If no explicit configuration exists, returns true (opt-out).
    /// </summary>
    Task<bool> IsEnabledAsync(string tenantId, string featureKey, CancellationToken ct = default);

    /// <summary>
    /// Resolves the feature key corresponding to a SAP object type.
    /// </summary>
    string ResolveFeatureKey(string objectType);
}
