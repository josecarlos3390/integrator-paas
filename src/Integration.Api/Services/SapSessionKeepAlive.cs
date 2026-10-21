using Integration.Shared.Clients;
using Integration.Shared.Configuration;
using Microsoft.Extensions.Options;

namespace Integration.Api.Services;

/// <summary>
/// Mantiene viva la sesión del Service Layer. SAP la cierra a los 30 minutos de
/// inactividad y el siguiente pedido paga el login completo (~3,5 s). Un ping
/// cada 10 minutos evita esa espera al primer mensaje del día.
/// </summary>
public class SapSessionKeepAlive : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    private readonly ITenantClientFactory _clientFactory;
    private readonly string _tenantId;
    private readonly ILogger<SapSessionKeepAlive> _logger;

    public SapSessionKeepAlive(
        ITenantClientFactory clientFactory,
        IOptions<TenantsConfig> tenantsConfig,
        ILogger<SapSessionKeepAlive> logger)
    {
        _clientFactory = clientFactory;
        _tenantId = tenantsConfig.Value.DefaultTenantId;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = await _clientFactory.GetSapClientAsync(_tenantId);
                await client.PingAsync(stoppingToken);
                _logger.LogDebug("Sesión SAP renovada para {Tenant}", _tenantId);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo mantener la sesión SAP");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
