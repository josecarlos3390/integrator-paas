using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Integration.Shared.Repositories;

public class TenantConfigRepository
{
    private readonly IntegrationDbContext _dbContext;

    public TenantConfigRepository(IntegrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TenantConfig?> GetByApiKeyHashAsync(string apiKeyHash, CancellationToken ct = default)
    {
        return await _dbContext.TenantConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.ApiKeyHash == apiKeyHash && t.IsActive, ct);
    }

    public async Task<TenantConfig?> GetByIdAsync(string tenantId, CancellationToken ct = default)
    {
        return await _dbContext.TenantConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.IsActive, ct);
    }

    /// <summary>
    /// Returns the tenant row regardless of its IsActive flag. Used by the
    /// master administration and by licensing enforcement (an inactive tenant
    /// must be distinguishable from a missing one, which falls back to global config).
    /// </summary>
    public async Task<TenantConfig?> GetByIdIncludingInactiveAsync(string tenantId, CancellationToken ct = default)
    {
        return await _dbContext.TenantConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId, ct);
    }

    public async Task<IReadOnlyList<TenantConfig>> GetAllAsync(CancellationToken ct = default)
    {
        return await _dbContext.TenantConfigs
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
    }

    /// <summary>Lists all tenants including inactive ones (master administration).</summary>
    public async Task<IReadOnlyList<TenantConfig>> GetAllIncludingInactiveAsync(CancellationToken ct = default)
    {
        return await _dbContext.TenantConfigs
            .AsNoTracking()
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
    }

    public async Task CreateAsync(TenantConfig config, CancellationToken ct = default)
    {
        _dbContext.TenantConfigs.Add(config);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(TenantConfig config, CancellationToken ct = default)
    {
        var tracked = await _dbContext.TenantConfigs.FindAsync(new object[] { config.TenantId }, ct);
        if (tracked is null)
        {
            _dbContext.TenantConfigs.Update(config);
        }
        else
        {
            _dbContext.Entry(tracked).CurrentValues.SetValues(config);
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task SetActiveAsync(string tenantId, bool isActive, CancellationToken ct = default)
    {
        var config = await _dbContext.TenantConfigs.FindAsync(new object[] { tenantId }, ct);
        if (config is null)
            return;

        config.IsActive = isActive;
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateApiKeyHashAsync(string tenantId, string apiKeyHash, CancellationToken ct = default)
    {
        var config = await _dbContext.TenantConfigs.FindAsync(new object[] { tenantId }, ct);
        if (config is null)
            return;

        config.ApiKeyHash = apiKeyHash;
        await _dbContext.SaveChangesAsync(ct);
    }
}
