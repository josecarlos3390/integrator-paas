using System.Threading;
using Integration.Shared.Configuration;
using Integration.Shared.Infrastructure;
using Integration.Shared.Observability;
using Integration.Shared.Repositories;
using Integration.Worker.Dispatchers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Integration.Worker.Workers;

/// <summary>
/// BackgroundService that runs the HanaOutboxDispatcher in a loop
/// with configurable interval (default every 5 seconds).
/// Implements graceful shutdown: allows the current cycle to finish
/// before stopping (up to a 30s timeout).
/// Creates a new service scope per cycle to avoid sharing DbContext.
/// </summary>
public class OutboxDispatcherWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OutboxConfig> _config;
    private readonly ILogger<OutboxDispatcherWorker> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private CancellationTokenSource? _cycleCts;

    public OutboxDispatcherWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxConfig> config,
        IHostApplicationLifetime lifetime,
        ILogger<OutboxDispatcherWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _lifetime = lifetime;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxDispatcherWorker started with polling interval {Seconds}s", _config.Value.PollingSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Create a new scope per cycle so DbContext and repositories are fresh
                using var scope = _scopeFactory.CreateScope();

                // Create a combined CTS so the current cycle can be
                // cancelled if the host requests shutdown.
                // Interlocked.Exchange ensures thread-safe assignment.
                var newCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var previousCts = Interlocked.Exchange(ref _cycleCts, newCts);
                previousCts?.Dispose();

                // Poll every configured HANA server. Events carry TENANT_ID,
                // so a failure in one server must not stop the others.
                var registry = scope.ServiceProvider.GetRequiredService<HanaConnectionPoolRegistry>();
                var outboxOptions = scope.ServiceProvider.GetRequiredService<IOptions<OutboxConfig>>();
                var repoLogger = scope.ServiceProvider.GetRequiredService<ILogger<HanaOutboxRepository>>();

                foreach (var (serverName, pool) in registry.GetAll())
                {
                    try
                    {
                        var repo = new HanaOutboxRepository(pool, outboxOptions, repoLogger);
                        var dispatcher = ActivatorUtilities.CreateInstance<HanaOutboxDispatcher>(scope.ServiceProvider, repo);
                        dispatcher.ServerName = serverName;
                        await dispatcher.RunCycleAsync(_cycleCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Unhandled exception in OutboxDispatcherWorker cycle for HANA server {ServerName}", serverName);
                    }
                }

                // Flush runtime metrics to PostgreSQL so the API dashboard can read them
                try
                {
                    var deltas = MetricsSnapshot.CaptureDeltas();
                    if (deltas.Count > 0)
                    {
                        var metricRepo = scope.ServiceProvider.GetRequiredService<MetricRepository>();
                        await metricRepo.FlushAsync(deltas, _cycleCts.Token);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to flush runtime metrics to PostgreSQL");
                }
            }
            catch (OperationCanceledException)
            {
                // Shutdown requested during the cycle
                _logger.LogInformation("OutboxDispatcherWorker cycle cancelled due to shutdown request");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in OutboxDispatcherWorker cycle");
            }
            finally
            {
                // Thread-safe cleanup: swap out the current CTS and dispose it.
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

        _logger.LogInformation("OutboxDispatcherWorker stopped");
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("OutboxDispatcherWorker received stop signal. Waiting up to 30s for current cycle to complete...");

        // Give time for the current cycle to finish naturally
        var shutdownTask = base.StopAsync(cancellationToken);
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);

        var completed = await Task.WhenAny(shutdownTask, timeoutTask);
        if (completed == timeoutTask)
        {
            _logger.LogWarning("OutboxDispatcherWorker stop timed out after 30s. Forcing cancellation.");
            Interlocked.Exchange(ref _cycleCts, null)?.Cancel();
            try { await shutdownTask; } catch { /* best effort */ }
        }
        else
        {
            _logger.LogInformation("OutboxDispatcherWorker stopped gracefully");
        }
    }
}
