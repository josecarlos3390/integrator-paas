using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Integration.Shared.Repositories;

public class DeadLetterRepository
{
    private readonly IntegrationDbContext _dbContext;

    public DeadLetterRepository(IntegrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(DeadLetterEvent deadLetter, CancellationToken ct = default)
    {
        _dbContext.DeadLetterEvents.Add(deadLetter);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<(IReadOnlyList<DeadLetterEvent> Items, int TotalCount)> GetByTenantAsync(
        string tenantId,
        int skip = 0,
        int take = 25,
        CancellationToken ct = default)
    {
        var query = _dbContext.DeadLetterEvents
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId);

        var totalCount = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(d => d.DeadLetteredAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public async Task<DeadLetterEvent?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.DeadLetterEvents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == id, ct);
    }

    /// <summary>
    /// Gets recent DLQ events that could be candidates for automatic retry.
    /// </summary>
    public async Task<IReadOnlyList<DeadLetterEvent>> GetRetryableAsync(
        DateTime since,
        int take = 50,
        CancellationToken ct = default)
    {
        return await _dbContext.DeadLetterEvents
            .AsNoTracking()
            .Where(d => d.DeadLetteredAt >= since)
            .OrderBy(d => d.DeadLetteredAt)
            .Take(take)
            .ToListAsync(ct);
    }
}
