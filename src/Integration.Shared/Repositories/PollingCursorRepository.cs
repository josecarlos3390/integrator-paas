using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Integration.Shared.Repositories;

public class PollingCursorRepository
{
    private readonly IntegrationDbContext _dbContext;

    public PollingCursorRepository(IntegrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PollingCursor?> GetAsync(string tenantId, string entityType, CancellationToken ct = default)
    {
        return await _dbContext.PollingCursors
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.EntityType == entityType, ct);
    }

    public async Task UpsertAsync(PollingCursor cursor, CancellationToken ct = default)
    {
        var existing = await _dbContext.PollingCursors
            .FirstOrDefaultAsync(c => c.TenantId == cursor.TenantId && c.EntityType == cursor.EntityType, ct);

        if (existing != null)
        {
            existing.LastUpdateDate = cursor.LastUpdateDate;
            existing.LastUpdateTs = cursor.LastUpdateTs;
            existing.LastRunAt = cursor.LastRunAt;
        }
        else
        {
            _dbContext.PollingCursors.Add(cursor);
        }

        await _dbContext.SaveChangesAsync(ct);
    }
}
