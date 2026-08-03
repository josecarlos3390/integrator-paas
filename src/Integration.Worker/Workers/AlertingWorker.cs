using Integration.Shared.Configuration;
using Integration.Shared.Domain;
using Integration.Shared.Repositories;
using Integration.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Integration.Worker.Workers;

/// <summary>
/// BackgroundService that periodically monitors integration metrics
/// and generates alerts when configured thresholds are exceeded.
/// </summary>
public class AlertingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<AlertingConfig> _config;
    private readonly ILogger<AlertingWorker> _logger;

    public AlertingWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<AlertingConfig> config,
        ILogger<AlertingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Alerting Worker started with interval {Minutes} minutes", _config.Value.CheckIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckMetricsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in Alerting Worker cycle");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_config.Value.CheckIntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Alerting Worker stopping");
    }

    private async Task CheckMetricsAsync(CancellationToken ct)
    {
        if (!_config.Value.Enabled) return;

        using var scope = _scopeFactory.CreateScope();
        var logRepo = scope.ServiceProvider.GetRequiredService<IntegrationLogRepository>();
        var alertingService = scope.ServiceProvider.GetRequiredService<IAlertingService>();

        var window = TimeSpan.FromMinutes(_config.Value.ErrorRateWindowMinutes);
        var from = DateTime.UtcNow.Subtract(window);

        // Get recent logs grouped by tenant
        var recentLogs = await logRepo.GetRecentAsync(from, DateTime.UtcNow, 1000, ct);
        var grouped = recentLogs.GroupBy(l => l.TenantId);

        foreach (var tenantGroup in grouped)
        {
            var tenantId = tenantGroup.Key;
            var logs = tenantGroup.ToList();

            var deadLetters = logs.Count(l => l.Status == "dead_letter");
            var errors = logs.Count(l => l.Status == "error");

            if (deadLetters >= _config.Value.DeadLetterThreshold)
            {
                await alertingService.RaiseAlertAsync(
                    AlertType.DeadLetter,
                    AlertSeverity.Critical,
                    tenantId,
                    "Dead Letter Queue threshold exceeded",
                    $"{deadLetters} dead letter events in the last {window.TotalMinutes:F0} minutes",
                    ct: ct);
            }

            if (errors >= _config.Value.ErrorRateThreshold)
            {
                await alertingService.RaiseAlertAsync(
                    AlertType.HighErrorRate,
                    AlertSeverity.Warning,
                    tenantId,
                    "High transient error rate detected",
                    $"{errors} errors in the last {window.TotalMinutes:F0} minutes",
                    ct: ct);
            }
        }
    }
}
