using Integration.Shared.Domain;
using Integration.Shared.Observability;
using Integration.Shared.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Integration.Api.Controllers;

/// <summary>
/// Operations dashboard to monitor the SAP↔CRM integration flow.
/// Exposes REST endpoints consumed by the HTML/JS frontend.
/// </summary>
[ApiController]
[Route("api/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly HanaOutboxRepository _hanaRepo;
    private readonly IntegrationLogRepository _logRepo;
    private readonly DeadLetterRepository _dlqRepo;
    private readonly MetricRepository _metricRepo;
    private readonly TenantConfigRepository _tenantRepo;
    private readonly IntegrationRequestRepository _requestRepo;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        HanaOutboxRepository hanaRepo,
        IntegrationLogRepository logRepo,
        DeadLetterRepository dlqRepo,
        MetricRepository metricRepo,
        TenantConfigRepository tenantRepo,
        IntegrationRequestRepository requestRepo,
        ILogger<DashboardController> logger)
    {
        _hanaRepo = hanaRepo;
        _logRepo = logRepo;
        _dlqRepo = dlqRepo;
        _metricRepo = metricRepo;
        _tenantRepo = tenantRepo;
        _requestRepo = requestRepo;
        _logger = logger;
    }

    /// <summary>
    /// Returns the active tenant context for the dashboard header.
    /// If no API key is provided, returns the default tenant.
    /// </summary>
    [HttpGet("tenant-info")]
    public async Task<IActionResult> GetTenantInfo(CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString();
        if (!string.IsNullOrEmpty(tenantId))
        {
            var tenant = await _tenantRepo.GetByIdAsync(tenantId, ct);
            if (tenant != null)
                return Ok(new { tenant.TenantId, tenant.Name, tenant.IsActive });
        }

        var all = await _tenantRepo.GetAllAsync(ct);
        var def = all.FirstOrDefault(t => t.IsActive) ?? all.FirstOrDefault();
        if (def == null)
            return Ok(new { TenantId = "unknown", Name = "Sin tenant", IsActive = false });

        return Ok(new { def.TenantId, def.Name, def.IsActive });
    }

    /// <summary>
    /// Lists HANA outbox events with optional filters.
    /// </summary>
    [HttpGet("events")]
    public async Task<IActionResult> GetEvents(
        [FromQuery] string? eventType = null,
        [FromQuery] string? objectType = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        try
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var skip = (page - 1) * pageSize;
            var (events, totalCount) = await _hanaRepo.FetchAllAsync(eventType, objectType, status, skip, pageSize, ct);
            return Ok(new { Items = events, TotalCount = totalCount, Page = page, PageSize = pageSize });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch events from HANA");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Gets runtime business metrics.
    /// Business metrics (events, dead letters, retries, latencies) are calculated
    /// from integration_logs over a sliding window (default 24h).
    /// Technical metrics (circuit breaker, token refresh, feature flags) are read
    /// from accumulated counters in PostgreSQL.
    /// </summary>
    [HttpGet("metrics")]
    public async Task<IActionResult> GetMetrics(
        [FromQuery] int hours = 24,
        CancellationToken ct = default)
    {
        try
        {
            hours = Math.Clamp(hours, 1, 168); // 1 hour .. 7 days
            var from = DateTime.UtcNow.AddHours(-hours);
            var to = DateTime.UtcNow;

            // Business metrics from integration_logs (windowed)
            var totalEvents = await _metricRepo.GetTotalEventsProcessedAsync(from, to, ct);
            var statusCounts = await _metricRepo.GetStatusCountsAsync(from, to, ct);
            var eventTypeCounts = await _metricRepo.GetEventTypeCountsAsync(from, to, ct);
            var deadLetterCounts = await _metricRepo.GetDeadLetterCountsAsync(from, to, ct);
            var retryCounts = await _metricRepo.GetRetryCountsAsync(from, to, ct);
            var latencySummary = await _metricRepo.GetLatencyStatsAsync(from, to, 5000, ct);

            // Technical metrics from accumulated counters (totals since table creation)
            var counters = await _metricRepo.GetAllAsync(ct);

            var summary = new MetricsSummary
            {
                TotalEventsProcessed = totalEvents,
                TotalDeadLetters = statusCounts.GetValueOrDefault("dead_letter"),
                TotalRetries = retryCounts.Values.Sum(),
                TotalCircuitBreakerChanges = counters.GetValueOrDefault("total_circuit_breaker_changes"),
                TotalTokenRefreshes = counters.GetValueOrDefault("total_token_refreshes"),
                TotalFeatureFlagDecisions = counters.GetValueOrDefault("total_feature_flag_decisions"),
                EventsByStatus = statusCounts,
                EventsByType = eventTypeCounts,
                DeadLettersByCategory = deadLetterCounts,
                RetriesByType = retryCounts,
                CircuitBreakerBySystem = ExtractPrefix(counters, "circuit_system:"),
                TokenRefreshesByTenant = ExtractPrefix(counters, "token_refresh_tenant:"),
                LatencySummary = latencySummary,
                WindowHours = hours
            };

            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch runtime metrics");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    private static Dictionary<string, long> ExtractPrefix(Dictionary<string, long> counters, string prefix)
    {
        return counters
            .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(kvp => kvp.Key[prefix.Length..], kvp => kvp.Value);
    }

    /// <summary>
    /// Gets event statistics from HANA.
    /// </summary>
    [HttpGet("stats")]
    public async Task<IActionResult> GetStats(CancellationToken ct = default)
    {
        try
        {
            var stats = await _hanaRepo.FetchStatsAsync(ct);

            // Add idempotency hits from the last 24h
            var from = DateTime.UtcNow.AddDays(-1);
            var logs = await _logRepo.GetRecentAsync(from, DateTime.UtcNow, 1000, ct);
            var idempotencyHits = logs.Count(l => l.Status == "idempotency_hit");

            return Ok(new
            {
                stats.Total,
                stats.Pending,
                stats.Processed,
                stats.DeadLetter,
                stats.Failed,
                idempotencyHits
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch stats from HANA");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Lists execution logs from PostgreSQL.
    /// </summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] string? direction = null,
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        try
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var skip = (page - 1) * pageSize;
            var (logs, totalCount) = await _logRepo.GetRecentAsync(direction, status, skip, pageSize, ct);
            return Ok(new { Items = logs, TotalCount = totalCount, Page = page, PageSize = pageSize });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch logs from PostgreSQL");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Reprocesses an outbox event by its ID.
    /// </summary>
    [HttpPost("retry")]
    public async Task<IActionResult> RetryEvent(
        [FromQuery] string eventId,
        CancellationToken ct = default)
    {
        try
        {
            // Find the event in HANA to get the AggregateId
            var (events, _) = await _hanaRepo.FetchAllAsync(take: 1, ct: ct);
            var evt = events.FirstOrDefault(e => e.Id == eventId);

            if (evt == null)
                return NotFound(new { Message = "Event not found in HANA" });

            // Reset for retry
            await _hanaRepo.ResetForRetryAsync(evt.AggregateId, ct);
            return Ok(new { Message = "Event queued for retry", EventId = eventId, AggregateId = evt.AggregateId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry event {EventId}", eventId);
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Lists Dead Letter events from PostgreSQL.
    /// </summary>
    [HttpGet("dead-letters")]
    public async Task<IActionResult> GetDeadLetters(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        try
        {
            var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? "tenant-001";
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var skip = (page - 1) * pageSize;
            var (events, totalCount) = await _dlqRepo.GetByTenantAsync(tenantId, skip, pageSize, ct);
            return Ok(new { Items = events, TotalCount = totalCount, Page = page, PageSize = pageSize });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch dead letters");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Lists integration requests from the Data Ingestor.
    /// </summary>
    [HttpGet("ingestor/requests")]
    public async Task<IActionResult> GetIngestorRequests(
        [FromQuery] string? status = null,
        [FromQuery] string? sourceSystem = null,
        [FromQuery] string? targetSystem = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        try
        {
            var tenantId = HttpContext.Items["TenantId"]?.ToString();
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 100);
            var skip = (page - 1) * pageSize;
            var (requests, totalCount) = await _requestRepo.GetRecentAsync(
                tenantId, status, sourceSystem, targetSystem, skip, pageSize, ct);
            return Ok(new { Items = requests, TotalCount = totalCount, Page = page, PageSize = pageSize });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch integration requests");
            return StatusCode(500, new { Error = ex.Message });
        }
    }
}
