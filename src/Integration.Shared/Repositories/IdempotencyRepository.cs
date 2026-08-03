using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Integration.Shared.Repositories;

/// <summary>
/// Idempotency records repository.
/// </summary>
public class IdempotencyRepository
{
    private readonly IntegrationDbContext _dbContext;

    public IdempotencyRepository(IntegrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IdempotencyRecord?> GetAsync(
        string tenantId,
        string eventType,
        string aggregateId,
        CancellationToken ct = default)
    {
        return await _dbContext.IdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r =>
                r.TenantId == tenantId &&
                r.EventType == eventType &&
                r.AggregateId == aggregateId, ct);
    }

    public async Task UpsertAsync(IdempotencyRecord record, CancellationToken ct = default)
    {
        var existing = await _dbContext.IdempotencyRecords
            .FirstOrDefaultAsync(r =>
                r.TenantId == record.TenantId &&
                r.EventType == record.EventType &&
                r.AggregateId == record.AggregateId, ct);

        if (existing is null)
        {
            _dbContext.IdempotencyRecords.Add(record);
        }
        else
        {
            existing.Status = record.Status;
            existing.ProcessedAt = record.ProcessedAt;
            existing.ExpiresAt = record.ExpiresAt;
            _dbContext.IdempotencyRecords.Update(existing);
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<int> DeleteExpiredAsync(DateTime cutoff, CancellationToken ct = default)
    {
        var expired = await _dbContext.IdempotencyRecords
            .Where(r => r.ExpiresAt < cutoff)
            .ToListAsync(ct);

        if (expired.Count == 0) return 0;

        _dbContext.IdempotencyRecords.RemoveRange(expired);
        await _dbContext.SaveChangesAsync(ct);
        return expired.Count;
    }
}
