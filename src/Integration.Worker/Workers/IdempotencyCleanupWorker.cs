using Integration.Shared.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Integration.Worker.Workers;

/// <summary>
/// BackgroundService that cleans up expired idempotency records once a day.
/// </summary>
public class IdempotencyCleanupWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IdempotencyCleanupWorker> _logger;

    public IdempotencyCleanupWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<IdempotencyCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Idempotency Cleanup Worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var idempotencyService = scope.ServiceProvider.GetRequiredService<IIdempotencyService>();
                await idempotencyService.CleanupExpiredAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in Idempotency Cleanup Worker cycle");
            }

            try
            {
                // Wait 24 hours
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Idempotency Cleanup Worker stopping");
    }
}
