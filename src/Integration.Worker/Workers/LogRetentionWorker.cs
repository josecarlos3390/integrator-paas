using Integration.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Integration.Worker.Workers;

/// <summary>
/// Background service that purges old integration logs to prevent unbounded table growth.
/// Runs once per day and deletes logs older than the configured retention period (default 90 days).
/// </summary>
public class LogRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<LogRetentionWorker> _logger;
    private readonly TimeSpan _retention;
    private readonly TimeSpan _checkInterval;

    public LogRetentionWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<LogRetentionWorker> logger,
        TimeSpan? retention = null,
        TimeSpan? checkInterval = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _retention = retention ?? TimeSpan.FromDays(90);
        _checkInterval = checkInterval ?? TimeSpan.FromHours(24);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("LogRetentionWorker started. Retention: {RetentionDays} days. Interval: {IntervalHours}h.", _retention.TotalDays, _checkInterval.TotalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<IntegrationDbContext>();
                var cutoff = DateTime.UtcNow.Subtract(_retention);

                var deleted = await db.Database.ExecuteSqlInterpolatedAsync(
                    $"DELETE FROM integration_logs WHERE created_at < {cutoff}", stoppingToken);

                _logger.LogInformation("Deleted {Count} integration logs older than {Cutoff:u}", deleted, cutoff);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Log retention cleanup failed");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("LogRetentionWorker stopped");
    }
}
