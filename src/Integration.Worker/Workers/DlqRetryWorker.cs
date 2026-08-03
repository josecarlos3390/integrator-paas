using Integration.Shared.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Worker.Workers;

/// <summary>
/// BackgroundService that automatically retries Dead Letter events.
/// Only retries transient errors (5xx, timeout, circuit breaker, etc.).
/// Business errors (4xx, SAP not found) require manual intervention.
/// </summary>
public class DlqRetryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DlqRetryWorker> _logger;
    private readonly TimeSpan _retryInterval;
    private readonly HashSet<string> _transientErrorPatterns;

    public DlqRetryWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<DlqRetryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _retryInterval = TimeSpan.FromMinutes(15); // Configurable
        _transientErrorPatterns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "500", "502", "503", "504", "timeout", "circuit", "connection", "network",
            "unauthorized", "401", "prematurely", "response ended"
        };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DLQ Retry Worker started with interval {Minutes} minutes", _retryInterval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RetryEligibleEventsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in DLQ Retry Worker cycle");
            }

            try
            {
                await Task.Delay(_retryInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("DLQ Retry Worker stopping");
    }

    private async Task RetryEligibleEventsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dlqRepo = scope.ServiceProvider.GetRequiredService<DeadLetterRepository>();
        var hanaRepo = scope.ServiceProvider.GetRequiredService<HanaOutboxRepository>();

        // Get DLQ events from the last 24h that are transient
        var cutoff = DateTime.UtcNow.AddHours(-24);
        var candidates = await dlqRepo.GetRetryableAsync(cutoff, 50, ct);

        if (candidates.Count == 0) return;

        _logger.LogInformation("DLQ Retry Worker found {Count} events to evaluate", candidates.Count);

        foreach (var dlq in candidates)
        {
            if (!IsTransientError(dlq.ErrorMessage))
            {
                _logger.LogDebug("Skipping non-transient DLQ event {EventId}: {Error}", dlq.Id, dlq.ErrorMessage);
                continue;
            }

            try
            {
                // Reset in HANA so the OutboxDispatcherWorker processes it again
                await hanaRepo.ResetForRetryAsync(dlq.AggregateId, ct);
                _logger.LogInformation("DLQ event {EventId} (aggregate {AggregateId}) queued for retry", dlq.Id, dlq.AggregateId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retry DLQ event {EventId}", dlq.Id);
            }
        }
    }

    private bool IsTransientError(string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage)) return true; // Sin error definido = reintentar

        return _transientErrorPatterns.Any(pattern =>
            errorMessage.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}
