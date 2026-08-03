using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Integration.Shared.Repositories;

/// <summary>
/// Tenant feature flags repository.
/// </summary>
public class TenantFeatureFlagRepository
{
    private readonly IntegrationDbContext _dbContext;

    public TenantFeatureFlagRepository(IntegrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TenantFeatureFlag?> GetAsync(string tenantId, string featureKey, CancellationToken ct = default)
    {
        return await _dbContext.TenantFeatureFlags
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.FeatureKey == featureKey, ct);
    }

    public async Task<IReadOnlyList<TenantFeatureFlag>> GetByTenantAsync(string tenantId, CancellationToken ct = default)
    {
        return await _dbContext.TenantFeatureFlags
            .AsNoTracking()
            .Where(f => f.TenantId == tenantId)
            .OrderBy(f => f.FeatureKey)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TenantFeatureFlag>> GetAllAsync(CancellationToken ct = default)
    {
        return await _dbContext.TenantFeatureFlags
            .AsNoTracking()
            .OrderBy(f => f.TenantId).ThenBy(f => f.FeatureKey)
            .ToListAsync(ct);
    }

    public async Task SetAsync(string tenantId, string featureKey, bool isEnabled, CancellationToken ct = default)
    {
        var existing = await _dbContext.TenantFeatureFlags
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.FeatureKey == featureKey, ct);

        if (existing is null)
        {
            _dbContext.TenantFeatureFlags.Add(new TenantFeatureFlag
            {
                TenantId = tenantId,
                FeatureKey = featureKey,
                IsEnabled = isEnabled,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.IsEnabled = isEnabled;
            existing.UpdatedAt = DateTime.UtcNow;
            _dbContext.TenantFeatureFlags.Update(existing);
        }

        await _dbContext.SaveChangesAsync(ct);
    }
}
