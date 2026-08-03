using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Integration.Shared.Repositories;

/// <summary>
/// Repository for durable storage of inbound integration requests.
/// Used by the Data Ingestor endpoint and the IngestionWorker.
/// </summary>
public class IntegrationRequestRepository
{
    private readonly IntegrationDbContext _dbContext;

    public IntegrationRequestRepository(IntegrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public virtual async Task<IntegrationRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.IntegrationRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public virtual async Task CreateAsync(IntegrationRequest request, CancellationToken ct = default)
    {
        _dbContext.IntegrationRequests.Add(request);
        await _dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Fetches pending requests that are eligible for processing.
    /// Lease-based concurrency: only returns rows without an active lease.
    /// </summary>
    public virtual async Task<IReadOnlyList<IntegrationRequest>> FetchPendingAsync(
        int batchSize,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        return await _dbContext.IntegrationRequests
            .Where(r =>
                (r.Status == "received" || r.Status == "failed") &&
                (r.NextRetryAt == null || r.NextRetryAt <= now) &&
                (r.LeasedUntil == null || r.LeasedUntil <= now))
            .OrderBy(r => r.Priority)
            .ThenBy(r => r.ReceivedAt)
            .Take(batchSize)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Acquires a distributed lease on a batch of requests to prevent concurrent processing.
    /// Returns the IDs that were actually leased.
    /// </summary>
    public virtual async Task<IReadOnlyList<Guid>> AcquireLeaseAsync(
        IEnumerable<Guid> ids,
        TimeSpan leaseDuration,
        CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return Array.Empty<Guid>();

        var leasedUntil = DateTime.UtcNow.Add(leaseDuration);
        var now = DateTime.UtcNow;

        var rows = await _dbContext.IntegrationRequests
            .Where(r => idList.Contains(r.Id) && (r.LeasedUntil == null || r.LeasedUntil <= now))
            .ToListAsync(ct);

        foreach (var row in rows)
        {
            row.LeasedUntil = leasedUntil;
            row.Status = "processing";
        }

        await _dbContext.SaveChangesAsync(ct);
        return rows.Select(r => r.Id).ToList();
    }

    public virtual async Task CompleteAsync(
        Guid id,
        string? resultPayload,
        CancellationToken ct = default)
    {
        var row = await _dbContext.IntegrationRequests.FindAsync(new object[] { id }, ct);
        if (row == null) return;

        row.Status = "completed";
        row.ProcessedAt = DateTime.UtcNow;
        row.LeasedUntil = null;
        row.ResultPayload = resultPayload;
        await _dbContext.SaveChangesAsync(ct);
    }

    public virtual async Task FailAsync(
        Guid id,
        string error,
        TimeSpan retryDelay,
        CancellationToken ct = default)
    {
        var row = await _dbContext.IntegrationRequests.FindAsync(new object[] { id }, ct);
        if (row == null) return;

        row.Status = "failed";
        row.ErrorMessage = error;
        row.AttemptCount++;
        row.NextRetryAt = DateTime.UtcNow.Add(retryDelay);
        row.LeasedUntil = null;
        await _dbContext.SaveChangesAsync(ct);
    }

    public virtual async Task DeadLetterAsync(
        Guid id,
        string error,
        CancellationToken ct = default)
    {
        var row = await _dbContext.IntegrationRequests.FindAsync(new object[] { id }, ct);
        if (row == null) return;

        row.Status = "dead_letter";
        row.ErrorMessage = error;
        row.ProcessedAt = DateTime.UtcNow;
        row.LeasedUntil = null;
        await _dbContext.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Dashboard query with filters and pagination.
    /// </summary>
    public virtual async Task<(IReadOnlyList<IntegrationRequest> Items, int TotalCount)> GetRecentAsync(
        string? tenantId = null,
        string? status = null,
        string? sourceSystem = null,
        string? targetSystem = null,
        int skip = 0,
        int take = 25,
        CancellationToken ct = default)
    {
        var query = _dbContext.IntegrationRequests.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(tenantId))
            query = query.Where(r => r.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(r => r.Status == status);

        if (!string.IsNullOrWhiteSpace(sourceSystem))
            query = query.Where(r => r.SourceSystem == sourceSystem);

        if (!string.IsNullOrWhiteSpace(targetSystem))
            query = query.Where(r => r.TargetSystem == targetSystem);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(r => r.ReceivedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items, totalCount);
    }
}
