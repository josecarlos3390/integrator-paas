using Integration.Shared.Domain;
using Integration.Shared.Repositories;
using Integration.Shared.Services;
using Microsoft.AspNetCore.Mvc;

namespace Integration.Api.Controllers;

/// <summary>
/// Administration endpoints for support operations:
/// dead letters, integration logs, and tenant configuration.
/// </summary>
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly DeadLetterRepository _deadLetterRepo;
    private readonly IntegrationLogRepository _logRepo;
    private readonly TenantConfigRepository _tenantRepo;
    private readonly TenantFeatureFlagRepository _featureRepo;
    private readonly MetricRepository _metricRepo;
    private readonly IAlertingService _alertingService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        DeadLetterRepository deadLetterRepo,
        IntegrationLogRepository logRepo,
        TenantConfigRepository tenantRepo,
        TenantFeatureFlagRepository featureRepo,
        MetricRepository metricRepo,
        IAlertingService alertingService,
        ILogger<AdminController> logger)
    {
        _deadLetterRepo = deadLetterRepo;
        _logRepo = logRepo;
        _tenantRepo = tenantRepo;
        _featureRepo = featureRepo;
        _metricRepo = metricRepo;
        _alertingService = alertingService;
        _logger = logger;
    }

    /// <summary>
    /// Lists dead letter events for the current tenant.
    /// </summary>
    [HttpGet("dead-letters")]
    public async Task<IActionResult> GetDeadLetters(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? "tenant-001";
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var skip = (page - 1) * pageSize;
        var (items, totalCount) = await _deadLetterRepo.GetByTenantAsync(tenantId, skip, pageSize, ct);
        return Ok(new { Items = items, TotalCount = totalCount, Page = page, PageSize = pageSize });
    }

    /// <summary>
    /// Manually retry a dead-letter event.
    /// In a full implementation this would re-enqueue the event.
    /// </summary>
    [HttpPost("dead-letters/{id:guid}/retry")]
    public async Task<IActionResult> RetryDeadLetter(Guid id, CancellationToken ct)
    {
        var deadLetter = await _deadLetterRepo.GetByIdAsync(id, ct);
        if (deadLetter == null)
            return NotFound(new { Message = "Dead letter event not found" });

        // TODO: re-enqueue in HANA or RabbitMQ depending on the source
        _logger.LogInformation("Retry requested for dead-letter {Id} of type {EventType}", id, deadLetter.EventType);

        return Accepted(new { Message = "Retry queued", deadLetter.EventType, deadLetter.AggregateId });
    }

    /// <summary>
    /// Gets the latest integration logs for the tenant.
    /// </summary>
    [HttpGet("logs")]
    public async Task<IActionResult> GetLogs(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? "tenant-001";
        var start = from ?? DateTime.UtcNow.AddDays(-7);
        var end = to ?? DateTime.UtcNow;
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        // For this admin endpoint we use the existing GetByTenantAsync which already has take;
        // we quickly adapt it in the repo or use skip/take inline.
        var logs = await _logRepo.GetByTenantAsync(tenantId, start, end, skip + pageSize, ct);
        var pagedLogs = logs.Skip(skip).Take(pageSize).ToList();
        return Ok(new { Items = pagedLogs, TotalCount = logs.Count, Page = page, PageSize = pageSize });
    }

    /// <summary>
    /// Lists active tenants (without exposing secrets).
    /// </summary>
    [HttpGet("tenants")]
    public async Task<IActionResult> GetTenants(CancellationToken ct)
    {
        var tenants = await _tenantRepo.GetAllAsync(ct);
        var safe = tenants.Select(t => new
        {
            t.TenantId,
            t.Name,
            t.IsActive,
            t.CreatedAt
        });
        return Ok(safe);
    }

    /// <summary>
    /// Lists all feature flags.
    /// </summary>
    [HttpGet("features")]
    public async Task<IActionResult> GetAllFeatureFlags(CancellationToken ct)
    {
        var flags = await _featureRepo.GetAllAsync(ct);
        return Ok(flags);
    }

    /// <summary>
    /// Lists feature flags for a specific tenant.
    /// </summary>
    [HttpGet("features/{tenantId}")]
    public async Task<IActionResult> GetTenantFeatureFlags(string tenantId, CancellationToken ct)
    {
        var flags = await _featureRepo.GetByTenantAsync(tenantId, ct);
        return Ok(flags);
    }

    /// <summary>
    /// Enables or disables a feature flag for a tenant.
    /// </summary>
    [HttpPost("features/{tenantId}/{featureKey}")]
    public async Task<IActionResult> SetFeatureFlag(string tenantId, string featureKey, [FromBody] SetFeatureFlagRequest request, CancellationToken ct)
    {
        await _featureRepo.SetAsync(tenantId, featureKey, request.IsEnabled, ct);
        _logger.LogInformation("Feature flag {FeatureKey} for tenant {TenantId} set to {Enabled}", featureKey, tenantId, request.IsEnabled);
        return Ok(new { TenantId = tenantId, FeatureKey = featureKey, IsEnabled = request.IsEnabled });
    }

    // ========================================================================
    // Operational Alerts
    // ========================================================================

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlerts(
        [FromQuery] bool? active,
        [FromQuery] string? tenantId,
        [FromQuery] string? type,
        [FromQuery] string? severity,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var skip = (page - 1) * pageSize;

        (IReadOnlyList<IntegrationAlert> alerts, int totalCount) result;
        if (active == true)
        {
            AlertType? alertType = null;
            AlertSeverity? alertSeverity = null;
            if (!string.IsNullOrEmpty(type) && Enum.TryParse<AlertType>(type, true, out var at))
                alertType = at;
            if (!string.IsNullOrEmpty(severity) && Enum.TryParse<AlertSeverity>(severity, true, out var sev))
                alertSeverity = sev;

            result = await _alertingService.GetActiveAlertsAsync(tenantId, skip, pageSize, ct);
        }
        else
        {
            result = await _alertingService.GetRecentAlertsAsync(tenantId, skip, pageSize, ct);
        }
        return Ok(new { Items = result.alerts, TotalCount = result.totalCount, Page = page, PageSize = pageSize });
    }

    [HttpPost("alerts/{id:guid}/acknowledge")]
    public async Task<IActionResult> AcknowledgeAlert(Guid id, [FromBody] AcknowledgeAlertRequest? request, CancellationToken ct = default)
    {
        await _alertingService.AcknowledgeAlertAsync(id, request?.AcknowledgedBy, ct);
        return Ok(new { Message = "Alert acknowledged", Id = id });
    }

    [HttpGet("alerts/stats")]
    public async Task<IActionResult> GetAlertStats(CancellationToken ct = default)
    {
        var stats = await _alertingService.GetStatsAsync(ct);
        return Ok(stats);
    }

    /// <summary>
    /// Triggers a manual full sync of price lists.
    /// Reads all ITM1 from the beginning and generates PriceListChanged events.
    /// </summary>
    [HttpPost("price-lists/full-sync")]
    public async Task<IActionResult> TriggerPriceListFullSync(CancellationToken ct = default)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? "tenant-001";

        // Reset cursor to force full scan
        using var scope = HttpContext.RequestServices.CreateScope();
        var cursorRepo = scope.ServiceProvider.GetRequiredService<PollingCursorRepository>();
        await cursorRepo.UpsertAsync(new Integration.Shared.Domain.PollingCursor
        {
            TenantId = tenantId,
            EntityType = "PRICE_LIST",
            LastUpdateDate = DateTime.MinValue,
            LastUpdateTs = 0,
            LastRunAt = DateTime.UtcNow
        }, ct);

        return Accepted(new { Message = "Full sync triggered. PriceListPollingWorker will process all ITM1 on next cycle.", TenantId = tenantId });
    }

    /// <summary>
    /// Resets all accumulated technical metric counters (circuit breaker, token refresh, etc.).
    /// Business metrics (events, dead letters) are derived from logs and are not affected.
    /// </summary>
    [HttpPost("metrics/reset")]
    public async Task<IActionResult> ResetMetrics(CancellationToken ct = default)
    {
        await _metricRepo.ResetAllAsync(ct);
        _logger.LogInformation("Runtime metric counters reset by admin request");
        return Ok(new { Message = "Technical metric counters reset successfully" });
    }
}

public class SetFeatureFlagRequest
{
    public bool IsEnabled { get; set; }
}

public class AcknowledgeAlertRequest
{
    public string? AcknowledgedBy { get; set; }
}
