using Integration.Shared.Observability;
using Integration.Shared.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Integration.Shared.Services;

/// <summary>
/// Feature flag implementation with in-memory cache (TTL 30s)
/// to minimize queries to PostgreSQL during event processing.
/// Uses IServiceScopeFactory to resolve the scoped repository from the singleton.
/// </summary>
public class TenantFeatureService : ITenantFeatureService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantFeatureService> _logger;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    private static readonly Dictionary<string, string> ObjectTypeToFeatureKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["2"] = "BusinessPartnerSync",   // Business Partners
        ["4"] = "ItemSync",              // Items
        ["13"] = "InvoiceSync",          // Invoices
        ["17"] = "SalesOrderSync"        // Orders
    };

    public TenantFeatureService(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        ILogger<TenantFeatureService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<bool> IsEnabledAsync(string tenantId, string featureKey, CancellationToken ct = default)
    {
        var cacheKey = $"ff:{tenantId}:{featureKey}";
        if (_cache.TryGetValue(cacheKey, out bool cachedValue))
        {
            return cachedValue;
        }

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<TenantFeatureFlagRepository>();
        var flag = await repository.GetAsync(tenantId, featureKey, ct);
        var enabled = flag?.IsEnabled ?? true; // opt-out: habilitado por defecto

        _cache.Set(cacheKey, enabled, CacheTtl);
        IntegrationMetrics.RecordFeatureFlagDecision(tenantId, featureKey, enabled);
        _logger.LogDebug("Feature flag {FeatureKey} for tenant {TenantId} resolved to {Enabled}", featureKey, tenantId, enabled);

        return enabled;
    }

    public string ResolveFeatureKey(string objectType)
    {
        if (ObjectTypeToFeatureKey.TryGetValue(objectType, out var key))
            return key;

        // For unknown documents, we do not block; we use the objectType itself as key
        return objectType;
    }
}
