using System.Threading;
using Integration.Shared.Configuration;
using Integration.Shared.Repositories;
using Integration.Worker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Integration.Worker.Workers;

/// <summary>
/// BackgroundService that polls PostgreSQL integration_requests and processes them.
/// Lease-based concurrency prevents multiple worker instances from processing the same request.
/// Implements graceful shutdown with up to 30s timeout.
/// </summary>
public class IngestionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<IngestionConfig> _config;
    private readonly ILogger<IngestionWorker> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private CancellationTokenSource? _cycleCts;

    public IngestionWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<IngestionConfig> config,
        IHostApplicationLifetime lifetime,
        ILogger<IngestionWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("IngestionWorker started with polling interval {Seconds}s, batch size {BatchSize}",
            _config.Value.PollingSeconds, _config.Value.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var requestRepo = scope.ServiceProvider.GetRequiredService<IntegrationRequestRepository>();
                var processor = scope.ServiceProvider.GetRequiredService<IngestionProcessor>();

                var newCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var previousCts = Interlocked.Exchange(ref _cycleCts, newCts);
                previousCts?.Dispose();

                await RunCycleAsync(requestRepo, processor, _cycleCts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("IngestionWorker cycle cancelled due to shutdown request");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in IngestionWorker cycle");
            }
            finally
            {
                var cts = Interlocked.Exchange(ref _cycleCts, null);
                cts?.Dispose();
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_config.Value.PollingSeconds), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("IngestionWorker stopped");
    }

    private async Task RunCycleAsync(
        IntegrationRequestRepository requestRepo,
        IngestionProcessor processor,
        CancellationToken ct)
    {
        var batch = await requestRepo.FetchPendingAsync(_config.Value.BatchSize, ct);
        if (batch.Count == 0) return;

        var leaseDuration = TimeSpan.FromSeconds(_config.Value.PollingSeconds + 30);
        var leasedIds = await requestRepo.AcquireLeaseAsync(batch.Select(r => r.Id), leaseDuration, ct);

        if (leasedIds.Count == 0)
        {
            _logger.LogDebug("No integration requests could be leased. Another instance may be processing them.");
            return;
        }

        var leasedBatch = batch.Where(r => leasedIds.Contains(r.Id)).ToList();
        _logger.LogInformation("Processing {LeasedCount} integration requests", leasedBatch.Count);

        using var semaphore = new SemaphoreSlim(_config.Value.MaxConcurrency);
        var tasks = leasedBatch.Select(async request =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await processor.ProcessAsync(request, requestRepo, ct);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        await Task.WhenAll(tasks);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("IngestionWorker received stop signal. Waiting up to 30s for current cycle to complete...");

        var shutdownTask = base.StopAsync(cancellationToken);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

        var completed = await Task.WhenAny(shutdownTask, timeoutTask);
        if (completed == timeoutTask)
        {
            _logger.LogWarning("IngestionWorker stop timed out after 30s. Forcing cancellation.");
            Interlocked.Exchange(ref _cycleCts, null)?.Cancel();
            try { await shutdownTask; } catch { /* best effort */ }
        }
        else
        {
            _logger.LogInformation("IngestionWorker stopped gracefully");
        }
    }
}
