using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Integration.Shared.Repositories;

/// <summary>
/// Repository for writing and reading audit records in PostgreSQL.
/// </summary>
public class IntegrationLogRepository
{
    private readonly IntegrationDbContext _dbContext;

    public IntegrationLogRepository(IntegrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public virtual async Task AddAsync(IntegrationLog log, CancellationToken ct = default)
    {
        _dbContext.IntegrationLogs.Add(log);
        await _dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Batch insert multiple logs in a single database round-trip.
    /// Use at the end of a processing cycle to amortize transaction overhead.
    /// </summary>
    public async Task AddBatchAsync(IEnumerable<IntegrationLog> logs, CancellationToken ct = default)
    {
        var batch = logs.ToList();
        if (batch.Count == 0) return;

        _dbContext.IntegrationLogs.AddRange(batch);
        await _dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Returns the number of integration logs for a tenant within a time window.
    /// Used for tenant quota enforcement.
    /// </summary>
    public virtual async Task<long> GetProcessedCountByTenantAsync(
        string tenantId,
        DateTime from,
        CancellationToken ct = default)
    {
        return await _dbContext.IntegrationLogs
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.CreatedAt >= from)
            .LongCountAsync(ct);
    }

    public async Task<IReadOnlyList<IntegrationLog>> GetByTenantAsync(
        string tenantId,
        DateTime from,
        DateTime to,
        int take = 100,
        CancellationToken ct = default)
    {
        return await _dbContext.IntegrationLogs
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.CreatedAt >= from && l.CreatedAt <= to)
            .OrderByDescending(l => l.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Gets recent logs with optional filters and pagination (for dashboard).
    /// </summary>
    public async Task<(IReadOnlyList<IntegrationLog> Items, int TotalCount)> GetRecentAsync(
        string? direction = null,
        string? status = null,
        int skip = 0,
        int take = 25,
        CancellationToken ct = default)
    {
        var query = _dbContext.IntegrationLogs.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(direction))
            query = query.Where(l => l.Direction.ToString() == direction);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(l => l.Status == status);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(l => l.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    /// <summary>
    /// Gets logs in a date range (for alerting worker).
    /// </summary>
    public async Task<IReadOnlyList<IntegrationLog>> GetRecentAsync(
        DateTime from,
        DateTime to,
        int take = 1000,
        CancellationToken ct = default)
    {
        return await _dbContext.IntegrationLogs
            .AsNoTracking()
            .Where(l => l.CreatedAt >= from && l.CreatedAt <= to)
            .OrderByDescending(l => l.CreatedAt)
            .Take(take)
            .ToListAsync(ct);
    }
}
