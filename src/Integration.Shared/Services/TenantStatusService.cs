using Integration.Shared.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Integration.Shared.Services;

/// <summary>
/// Licensing enforcement per tenant: a tenant with a row in tenant_config and
/// IsActive = false must not be processed. A tenant WITHOUT a row falls back to
/// the global configuration and stays allowed (single-tenant setups). Result is
/// cached in memory (TTL 30s) to avoid a PostgreSQL query per event.
/// </summary>
public class TenantStatusService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantStatusService> _logger;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public TenantStatusService(
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache,
        ILogger<TenantStatusService> logger)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Returns false only when the tenant exists in tenant_config and is inactive.
    /// </summary>
    public virtual async Task<bool> IsLicensedAsync(string tenantId, CancellationToken ct = default)
    {
        var cacheKey = $"license:{tenantId}";
        if (_cache.TryGetValue(cacheKey, out bool cachedValue))
        {
            return cachedValue;
        }

        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<TenantConfigRepository>();
        var config = await repository.GetByIdIncludingInactiveAsync(tenantId, ct);
        var licensed = config is null || config.IsActive;

        _cache.Set(cacheKey, licensed, CacheTtl);
        if (!licensed)
        {
            _logger.LogWarning("Tenant {TenantId} is inactive (licensing); skipping processing", tenantId);
        }

        return licensed;
    }
}
