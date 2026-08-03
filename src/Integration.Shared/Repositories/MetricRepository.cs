using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Integration.Shared.Observability;
using Microsoft.EntityFrameworkCore;

namespace Integration.Shared.Repositories;

/// <summary>
/// Persists and reads runtime metric counters from PostgreSQL.
/// Designed for cross-process sharing (Worker writes, API reads).
/// Also provides windowed aggregates over integration_logs for dashboard metrics.
/// </summary>
public class MetricRepository
{
    private readonly IntegrationDbContext _dbContext;

    public MetricRepository(IntegrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // ========================================================================
    // Worker-side: flush ephemeral deltas to PostgreSQL
    // ========================================================================

    public async Task FlushAsync(Dictionary<string, long> deltas, CancellationToken ct = default)
    {
        if (deltas.Count == 0) return;

        foreach (var kvp in deltas)
        {
            if (kvp.Value <= 0) continue;

            await _dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO integration_metric_counters (metric_key, metric_value, updated_at)
                VALUES ({kvp.Key}, {kvp.Value}, {DateTime.UtcNow})
                ON CONFLICT (metric_key) DO UPDATE SET
                    metric_value = integration_metric_counters.metric_value + EXCLUDED.metric_value,
                    updated_at = EXCLUDED.updated_at
                """, ct);
        }
    }

    // ========================================================================
    // API-side: technical counters (circuit breaker, token refresh, etc.)
    // These are rare events; totals accumulated since table creation are useful.
    // ========================================================================

    public async Task<Dictionary<string, long>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await _dbContext.MetricCounters
            .AsNoTracking()
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.MetricKey, r => r.MetricValue);
    }

    public async Task ResetAllAsync(CancellationToken ct = default)
    {
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM integration_metric_counters", ct);
    }

    // ========================================================================
    // API-side: windowed business metrics from integration_logs
    // ========================================================================

    public async Task<long> GetTotalEventsProcessedAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await _dbContext.IntegrationLogs
            .AsNoTracking()
            .Where(l => l.CreatedAt >= from && l.CreatedAt <= to)
            .LongCountAsync(ct);
    }

    public async Task<Dictionary<string, long>> GetStatusCountsAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await _dbContext.IntegrationLogs
            .AsNoTracking()
            .Where(l => l.CreatedAt >= from && l.CreatedAt <= to)
            .GroupBy(l => l.Status)
            .Select(g => new { Status = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);
    }

    public async Task<Dictionary<string, long>> GetEventTypeCountsAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await _dbContext.IntegrationLogs
            .AsNoTracking()
            .Where(l => l.CreatedAt >= from && l.CreatedAt <= to)
            .GroupBy(l => l.EventType)
            .Select(g => new { EventType = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(x => x.EventType, x => x.Count, ct);
    }

    public async Task<Dictionary<string, long>> GetDeadLetterCountsAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await _dbContext.DeadLetterEvents
            .AsNoTracking()
            .Where(d => d.DeadLetteredAt >= from && d.DeadLetteredAt <= to)
            .GroupBy(d => d.EventType)
            .Select(g => new { EventType = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(x => x.EventType, x => x.Count, ct);
    }

    public async Task<Dictionary<string, long>> GetRetryCountsAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        return await _dbContext.IntegrationLogs
            .AsNoTracking()
            .Where(l => l.CreatedAt >= from && l.CreatedAt <= to && l.Status == "error")
            .GroupBy(l => l.EventType)
            .Select(g => new { EventType = g.Key, Count = g.LongCount() })
            .ToDictionaryAsync(x => x.EventType, x => x.Count, ct);
    }

    /// <summary>
    /// Calculates latency percentiles from integration_logs for the given window.
    /// Pulls data to memory (max 5000 rows) and computes stats in C# for portability.
    /// </summary>
    public async Task<Dictionary<string, LatencyStats>> GetLatencyStatsAsync(
        DateTime from,
        DateTime to,
        int take = 5000,
        CancellationToken ct = default)
    {
        var logs = await _dbContext.IntegrationLogs
            .AsNoTracking()
            .Where(l => l.CreatedAt >= from && l.CreatedAt <= to && l.DurationMs > 0)
            .OrderByDescending(l => l.CreatedAt)
            .Take(take)
            .ToListAsync(ct);

        var result = new Dictionary<string, LatencyStats>();
        var grouped = logs.GroupBy(l => l.EventType);
        foreach (var g in grouped)
        {
            var values = g.Select(l => (double)l.DurationMs).OrderBy(v => v).ToArray();
            if (values.Length == 0) continue;
            result[g.Key] = new LatencyStats
            {
                Count = values.Length,
                Min = Math.Round(values[0], 2),
                Max = Math.Round(values[^1], 2),
                Avg = Math.Round(values.Average(), 2),
                P50 = Math.Round(Percentile(values, 0.5), 2),
                P95 = Math.Round(Percentile(values, 0.95), 2),
                P99 = Math.Round(Percentile(values, 0.99), 2)
            };
        }
        return result;
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1) return sorted[0];
        var idx = (int)Math.Ceiling(sorted.Length * p) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
    }
}


