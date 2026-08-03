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

    public async Task<IReadOnlyList<TenantConfig>> GetAllAsync(CancellationToken ct = default)
    {
        return await _dbContext.TenantConfigs
            .AsNoTracking()
            .Where(t => t.IsActive)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);
    }
}
