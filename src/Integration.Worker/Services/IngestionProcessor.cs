using Integration.Shared.Configuration;
using Integration.Shared.Domain;
using Integration.Shared.Dtos;
using Integration.Shared.Observability;
using Integration.Shared.Repositories;
using Integration.Shared.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Refit;

namespace Integration.Worker.Services;

/// <summary>
/// Processes a single integration request: feature-flag check, quota check,
/// idempotency, routing, and result persistence.
/// </summary>
public class IngestionProcessor
{
    private readonly IRequestRouter _router;
    private readonly ITenantFeatureService _featureService;
    private readonly TenantQuotaRepository _quotaRepo;
    private readonly IntegrationLogRepository _logRepo;
    private readonly DeadLetterRepository _deadLetterRepo;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IAlertingService _alertingService;
    private readonly CallbackNotifier _notifier;
    private readonly IngestionConfig _config;
    private readonly ILogger<IngestionProcessor> _logger;

    public IngestionProcessor(
        IRequestRouter router,
        ITenantFeatureService featureService,
        TenantQuotaRepository quotaRepo,
        IntegrationLogRepository logRepo,
        DeadLetterRepository deadLetterRepo,
        IIdempotencyService idempotencyService,
        IAlertingService alertingService,
        CallbackNotifier notifier,
        IOptions<IngestionConfig> config,
        ILogger<IngestionProcessor> logger)
    {
        _router = router;
        _featureService = featureService;
        _quotaRepo = quotaRepo;
        _logRepo = logRepo;
        _deadLetterRepo = deadLetterRepo;
        _idempotencyService = idempotencyService;
        _alertingService = alertingService;
        _notifier = notifier;
        _config = config.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(
        IntegrationRequest request,
        IntegrationRequestRepository requestRepo,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        string? targetId = null;
        string? errorMessage = null;
        var status = "unknown";

        try
        {
            // 1. Feature flag check
            var featureKey = MapEntityTypeToFeatureKey(request.EntityType);
            if (!string.IsNullOrEmpty(featureKey))
            {
                var enabled = await _featureService.IsEnabledAsync(request.TenantId, featureKey, ct);
                if (!enabled)
                {
                    _logger.LogInformation(
                        "Request {RequestId} skipped: feature {FeatureKey} is disabled for tenant {TenantId}",
                        request.Id, featureKey, request.TenantId);

                    await FinalizeSkippedAsync(request, requestRepo, ct);
                    return;
                }
            }

            // 2. Quota check
            var quota = await _quotaRepo.GetAsync(request.TenantId, ct);
            if (quota != null && quota.MaxEventsPerHour > 0)
            {
                var since = DateTime.UtcNow.AddHours(-1);
                var count = await _logRepo.GetProcessedCountByTenantAsync(request.TenantId, since, ct);
                if (count >= quota.MaxEventsPerHour)
                {
                    _logger.LogWarning(
                        "Request {RequestId} delayed: tenant {TenantId} quota exceeded ({Count}/{Limit})",
                        request.Id, request.TenantId, count, quota.MaxEventsPerHour);

                    await FinalizeQuotaExceededAsync(request, requestRepo, ct);
                    return;
                }
            }

            // 3. Route check
            if (!_router.CanRoute(request.EntityType, request.TargetSystem))
            {
                _logger.LogWarning(
                    "Request {RequestId} cannot be routed: {EntityType} → {TargetSystem}",
                    request.Id, request.EntityType, request.TargetSystem);

                await FinalizeDeadLetterAsync(
                    request, requestRepo,
                    $"Route not supported: {request.EntityType} → {request.TargetSystem}",
                    "unsupported_route", ct);
                return;
            }

            // 4. Idempotency wraps ALL processing including logging, notification and status updates
            var idempotencyResult = await _idempotencyService.TryProcessAsync(
                request.TenantId,
                request.EntityType,
                request.ExternalId,
                async () =>
                {
                    targetId = await _router.RouteAsync(request, ct);
                },
                ct);

            if (idempotencyResult == IdempotencyResult.AlreadyProcessed)
            {
                _logger.LogInformation(
                    "Request {RequestId} already processed (idempotency hit)", request.Id);

                status = "idempotency_hit";
                await FinalizeSuccessAsync(request, requestRepo, targetId, status, sw.ElapsedMilliseconds, ct);
                return;
            }

            // 5. Success
            status = "success";
            await FinalizeSuccessAsync(request, requestRepo, targetId, status, sw.ElapsedMilliseconds, ct);

            _logger.LogInformation(
                "Request {RequestId} processed successfully. TargetId={TargetId}",
                request.Id, targetId);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Error processing request {RequestId}", request.Id);
            errorMessage = ex.Message;

            var isBusinessError = IsBusinessError(ex);

            if (isBusinessError || request.AttemptCount >= _config.MaxAttempts)
            {
                await FinalizeDeadLetterAsync(request, requestRepo, ex.Message, ClassifyError(ex), ct);
            }
            else
            {
                await FinalizeRetryAsync(request, requestRepo, ex.Message, ct);
            }
        }
    }

    private async Task FinalizeSkippedAsync(
        IntegrationRequest request,
        IntegrationRequestRepository requestRepo,
        CancellationToken ct)
    {
        await requestRepo.CompleteAsync(request.Id, null, ct);
        await LogAsync(request, "skipped", 0, null, ct);
        IntegrationMetrics.RecordEventProcessed(request.EntityType, request.TenantId, "skipped");
        await NotifyAsync(request, "skipped", null, ct);
    }

    private async Task FinalizeQuotaExceededAsync(
        IntegrationRequest request,
        IntegrationRequestRepository requestRepo,
        CancellationToken ct)
    {
        await requestRepo.FailAsync(request.Id, "quota_exceeded", TimeSpan.FromMinutes(5), ct);
        await LogAsync(request, "quota_exceeded", 0, "Tenant quota exceeded", ct);
        IntegrationMetrics.RecordEventProcessed(request.EntityType, request.TenantId, "quota_exceeded");
        // Do not notify on quota exceeded; the request will be retried automatically.
    }

    private async Task FinalizeSuccessAsync(
        IntegrationRequest request,
        IntegrationRequestRepository requestRepo,
        string? targetId,
        string status,
        long durationMs,
        CancellationToken ct)
    {
        await requestRepo.CompleteAsync(request.Id, targetId, ct);
        await LogAsync(request, status, durationMs, null, ct);
        IntegrationMetrics.RecordEventProcessed(request.EntityType, request.TenantId, status);
        IntegrationMetrics.RecordEventLatency(request.EntityType, request.TenantId, durationMs);
        await NotifyAsync(request, MapStatusForCallback(status), targetId, ct);
    }

    private async Task FinalizeDeadLetterAsync(
        IntegrationRequest request,
        IntegrationRequestRepository requestRepo,
        string errorMessage,
        string errorCategory,
        CancellationToken ct)
    {
        await requestRepo.DeadLetterAsync(request.Id, errorMessage, ct);
        await _deadLetterRepo.AddAsync(new DeadLetterEvent
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            Source = "ingestor",
            EventType = request.EntityType,
            AggregateId = request.ExternalId,
            Payload = request.Payload,
            ErrorMessage = errorMessage,
            AttemptCount = request.AttemptCount,
            OccurredAt = request.ReceivedAt,
            DeadLetteredAt = DateTime.UtcNow
        }, ct);
        await LogAsync(request, "dead_letter", 0, errorMessage, ct);
        IntegrationMetrics.RecordEventProcessed(request.EntityType, request.TenantId, "dead_letter");
        IntegrationMetrics.RecordDeadLetter(request.EntityType, request.TenantId, errorCategory);
        await NotifyAsync(request, "dead_letter", null, ct);

        await _alertingService.RaiseAlertAsync(
            AlertType.DeadLetter,
            AlertSeverity.Critical,
            request.TenantId,
            $"Ingestor dead letter: {request.EntityType}",
            $"Request {request.Id} for external id {request.ExternalId} failed permanently.",
            errorMessage,
            ct);
    }

    private async Task FinalizeRetryAsync(
        IntegrationRequest request,
        IntegrationRequestRepository requestRepo,
        string errorMessage,
        CancellationToken ct)
    {
        var delay = TimeSpan.FromSeconds(Math.Pow(_config.RetryBackoffMultiplier, request.AttemptCount + 1) * _config.RetryBaseDelaySeconds);
        await requestRepo.FailAsync(request.Id, errorMessage, delay, ct);
        await LogAsync(request, "error", 0, errorMessage, ct);
        IntegrationMetrics.RecordEventProcessed(request.EntityType, request.TenantId, "error");
        IntegrationMetrics.RecordRetry(request.EntityType, request.TenantId, request.AttemptCount + 1);
    }

    private async Task LogAsync(
        IntegrationRequest request,
        string status,
        long durationMs,
        string? error,
        CancellationToken ct)
    {
        var direction = request.TargetSystem.Equals("erp", StringComparison.OrdinalIgnoreCase)
            ? IntegrationDirection.CrmToSap
            : IntegrationDirection.SapToCrm;

        await _logRepo.AddAsync(new IntegrationLog
        {
            Id = Guid.NewGuid(),
            TenantId = request.TenantId,
            CorrelationId = request.CorrelationId,
            Direction = direction,
            EventType = request.EntityType,
            ExternalId = request.ExternalId,
            Status = status,
            ErrorMessage = error,
            DurationMs = durationMs,
            CreatedAt = DateTime.UtcNow
        }, ct);
    }

    private async Task NotifyAsync(
        IntegrationRequest request,
        string status,
        string? targetSystemId,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.CallbackUrl))
            return;

        var result = new IngestionResult
        {
            RequestId = request.Id.ToString(),
            CorrelationId = request.CorrelationId,
            Status = status,
            ExternalId = request.ExternalId,
            TargetSystemId = targetSystemId,
            ProcessedAt = DateTime.UtcNow
        };

        await _notifier.NotifyAsync(request.CallbackUrl, result, ct);
    }

    private static string MapStatusForCallback(string internalStatus)
    {
        return internalStatus switch
        {
            "idempotency_hit" => "completed",
            "skipped" => "skipped",
            "success" => "completed",
            "dead_letter" => "dead_letter",
            _ => internalStatus
        };
    }

    private static bool IsBusinessError(Exception ex)
    {
        return ex is Integration.Shared.Exceptions.SapIntegrationException sapEx && sapEx.IsBusinessError
            || ex is ApiException apiEx && (int)apiEx.StatusCode is >= 400 and < 500
            || ex is HttpRequestException { StatusCode: not null } hre && (int)hre.StatusCode.Value is >= 400 and < 500;
    }

    private static string ClassifyError(Exception ex)
    {
        return ex switch
        {
            Integration.Shared.Exceptions.SapIntegrationException sapEx when sapEx.IsBusinessError => "sap_business_error",
            ApiException apiEx => $"api_{(int)apiEx.StatusCode}",
            HttpRequestException => "http_request_error",
            TimeoutException => "timeout",
            NotSupportedException => "unsupported_route",
            _ => "unknown"
        };
    }

    private static string? MapEntityTypeToFeatureKey(string entityType)
    {
        return entityType.ToLowerInvariant() switch
        {
            "account" => "BusinessPartnerSync",
            "vendor" => "BusinessPartnerSync",
            "product" => "ItemSync",
            "invoice" => "InvoiceSync",
            "order" => "SalesOrderSync",
            "price_list" => "PriceListSync",
            "price_list_header" => "PriceListSync",
            _ => null
        };
    }
}
