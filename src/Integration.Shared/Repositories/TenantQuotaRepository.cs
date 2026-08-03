using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Integration.Shared.Repositories;

public class TenantQuotaRepository
{
    private readonly IntegrationDbContext _dbContext;

    public TenantQuotaRepository(IntegrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public virtual async Task<TenantQuota?> GetAsync(string tenantId, CancellationToken ct = default)
    {
        return await _dbContext.TenantQuotas
            .AsNoTracking()
            .FirstOrDefaultAsync(q => q.TenantId == tenantId, ct);
    }

    public async Task UpsertAsync(TenantQuota quota, CancellationToken ct = default)
    {
        var existing = await _dbContext.TenantQuotas.FindAsync(new object[] { quota.TenantId }, ct);
        if (existing is null)
        {
            _dbContext.TenantQuotas.Add(quota);
        }
        else
        {
            existing.MaxEventsPerHour = quota.MaxEventsPerHour;
            existing.MaxApiCallsPerMinute = quota.MaxApiCallsPerMinute;
            existing.UpdatedAt = quota.UpdatedAt;
        }
        await _dbContext.SaveChangesAsync(ct);
    }
}
