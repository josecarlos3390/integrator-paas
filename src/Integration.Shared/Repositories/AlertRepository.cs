using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Integration.Shared.Repositories;

/// <summary>
/// Operational alerts repository.
/// </summary>
public class AlertRepository
{
    private readonly IntegrationDbContext _dbContext;

    public AlertRepository(IntegrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(IntegrationAlert alert, CancellationToken ct = default)
    {
        _dbContext.Alerts.Add(alert);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IntegrationAlert?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _dbContext.Alerts.FindAsync(new object[] { id }, ct);
    }

    public async Task<(IReadOnlyList<IntegrationAlert> Items, int TotalCount)> GetActiveAsync(
        string? tenantId = null,
        AlertType? type = null,
        AlertSeverity? severity = null,
        int skip = 0,
        int take = 25,
        CancellationToken ct = default)
    {
        var query = _dbContext.Alerts.AsNoTracking()
            .Where(a => !a.IsAcknowledged);

        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(a => a.TenantId == tenantId);
        if (type.HasValue)
            query = query.Where(a => a.AlertType == type.Value);
        if (severity.HasValue)
            query = query.Where(a => a.Severity == severity.Value);

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);
        return (items, totalCount);
    }

    public async Task<(IReadOnlyList<IntegrationAlert> Items, int TotalCount)> GetRecentAsync(
        string? tenantId = null,
        int skip = 0,
        int take = 25,
        CancellationToken ct = default)
    {
        var query = _dbContext.Alerts.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(a => a.TenantId == tenantId);

        var totalCount = await query.CountAsync(ct);
        var items = await query.OrderByDescending(a => a.CreatedAt).Skip(skip).Take(take).ToListAsync(ct);
        return (items, totalCount);
    }

    public async Task<bool> HasRecentAlertAsync(
        string tenantId,
        AlertType alertType,
        TimeSpan window,
        CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow.Subtract(window);
        return await _dbContext.Alerts.AsNoTracking()
            .AnyAsync(a =>
                a.TenantId == tenantId &&
                a.AlertType == alertType &&
                !a.IsAcknowledged &&
                a.CreatedAt >= cutoff, ct);
    }

    public async Task AcknowledgeAsync(Guid id, string? acknowledgedBy, CancellationToken ct = default)
    {
        var alert = await _dbContext.Alerts.FindAsync(new object[] { id }, ct);
        if (alert == null) return;

        alert.IsAcknowledged = true;
        alert.AcknowledgedAt = DateTime.UtcNow;
        alert.AcknowledgedBy = acknowledgedBy;

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<object> GetStatsAsync(string? tenantId = null, CancellationToken ct = default)
    {
        var query = _dbContext.Alerts.AsNoTracking().Where(a => !a.IsAcknowledged);
        if (!string.IsNullOrWhiteSpace(tenantId))
            query = query.Where(a => a.TenantId == tenantId);

        var active = await query
            .GroupBy(a => a.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var totalActive = active.Sum(a => a.Count);
        var critical = active.FirstOrDefault(a => a.Severity == AlertSeverity.Critical)?.Count ?? 0;
        var warning = active.FirstOrDefault(a => a.Severity == AlertSeverity.Warning)?.Count ?? 0;

        return new { totalActive, critical, warning };
    }
}
