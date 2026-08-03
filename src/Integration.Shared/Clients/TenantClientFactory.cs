using System.Collections.Concurrent;
using System.Net;
using Integration.Shared.Configuration;
using Integration.Shared.Connectors;
using Integration.Shared.Connectors.HansaCrm;
using Integration.Shared.Infrastructure;
using Integration.Shared.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Refit;

namespace Integration.Shared.Clients;

/// <summary>
/// Implementation of ITenantClientFactory with in-memory cache per tenant.
/// Each tenant gets its own HttpClient (with isolated cookie container for SAP)
/// and its own ICrmConnector instance based on the tenant's CrmConnectorType.
/// Falls back to global appsettings.json configuration if the tenant has no own values.
/// 
/// CRITICAL: Polly resilience pipelines are created PER TENANT to avoid circuit breaker
/// cross-talk between tenants (one tenant's failures must not block another).
/// </summary>
public class TenantClientFactory : ITenantClientFactory
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<SapConfig> _globalSapConfig;
    private readonly IOptions<CrmConfig> _globalCrmConfig;
    private readonly IOptions<HansaCrmConfig> _hansaCrmConfig;
    private readonly IMemoryCache _cache;
    private readonly IHttpMessageHandlerFactory _handlerFactory;
    private readonly ILoggerFactory _loggerFactory;

    private readonly ConcurrentDictionary<string, Lazy<Task<ServiceLayerClient>>> _sapClients = new();
    private readonly ConcurrentDictionary<string, Lazy<Task<ICrmConnector>>> _crmConnectors = new();

    // Per-tenant Polly pipelines — isolated circuit breakers (SaaS requirement)
    private readonly ConcurrentDictionary<string, ResiliencePipeline<HttpResponseMessage>> _sapPipelines = new();
    private readonly ConcurrentDictionary<string, ResiliencePipeline<HttpResponseMessage>> _crmPipelines = new();

    public TenantClientFactory(
        IServiceScopeFactory scopeFactory,
        IOptions<SapConfig> globalSapConfig,
        IOptions<CrmConfig> globalCrmConfig,
        IOptions<HansaCrmConfig> hansaCrmConfig,
        IMemoryCache cache,
        IHttpMessageHandlerFactory handlerFactory,
        ILoggerFactory loggerFactory)
    {
        _scopeFactory = scopeFactory;
        _globalSapConfig = globalSapConfig;
        _globalCrmConfig = globalCrmConfig;
        _hansaCrmConfig = hansaCrmConfig;
        _cache = cache;
        _handlerFactory = handlerFactory;
        _loggerFactory = loggerFactory;
    }

    public Task<ServiceLayerClient> GetSapClientAsync(string tenantId)
    {
        var lazy = _sapClients.GetOrAdd(tenantId, id => new Lazy<Task<ServiceLayerClient>>(() => CreateSapClientAsync(id)));
        return lazy.Value;
    }

    public Task<ICrmConnector> GetCrmConnectorAsync(string tenantId)
    {
        var lazy = _crmConnectors.GetOrAdd(tenantId, id => new Lazy<Task<ICrmConnector>>(() => CreateCrmConnectorAsync(id)));
        return lazy.Value;
    }

    private async Task<ServiceLayerClient> CreateSapClientAsync(string tenantId)
    {
        var tenantConfig = await GetTenantConfigAsync(tenantId);

        var sapConfig = new SapConfig
        {
            ServiceLayerUrl = !string.IsNullOrWhiteSpace(tenantConfig?.SapServiceLayerUrl)
                ? tenantConfig.SapServiceLayerUrl
                : _globalSapConfig.Value.ServiceLayerUrl,
            CompanyDB = !string.IsNullOrWhiteSpace(tenantConfig?.SapCompanyDb)
                ? tenantConfig.SapCompanyDb
                : _globalSapConfig.Value.CompanyDB,
            UserName = !string.IsNullOrWhiteSpace(tenantConfig?.SapUserName)
                ? tenantConfig.SapUserName
                : _globalSapConfig.Value.UserName,
            Password = !string.IsNullOrWhiteSpace(tenantConfig?.SapPasswordEncrypted)
                ? tenantConfig.SapPasswordEncrypted
                : _globalSapConfig.Value.Password
        };

        // Build or reuse an isolated Polly pipeline for this tenant
        var pipeline = _sapPipelines.GetOrAdd(tenantId, _ => ResiliencePolicies.BuildSapPipeline());

        // Use IHttpMessageHandlerFactory for DNS rotation and connection pooling,
        // then wrap with per-tenant CookieContainer + Polly.
        var baseHandler = _handlerFactory.CreateHandler("sap-base");
        var cookieHandler = new CookieContainerHandler(new CookieContainer()) { InnerHandler = baseHandler };
        var pollyHandler = new PollyResilienceHandler(pipeline) { InnerHandler = cookieHandler };

        var httpClient = new HttpClient(pollyHandler)
        {
            BaseAddress = new Uri(sapConfig.ServiceLayerUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        var logger = _loggerFactory.CreateLogger<ServiceLayerClient>();
        return new ServiceLayerClient(httpClient, sapConfig, logger);
    }

    private async Task<ICrmConnector> CreateCrmConnectorAsync(string tenantId)
    {
        var tenantConfig = await GetTenantConfigAsync(tenantId);
        var connectorType = tenantConfig?.CrmConnectorType ?? CrmConnectorType.Mock;

        return connectorType switch
        {
            CrmConnectorType.HansaCrm => await CreateHansaCrmConnectorAsync(tenantId, tenantConfig),
            _ => await CreateMockCrmConnectorAsync(tenantId, tenantConfig)
        };
    }

    private async Task<ICrmConnector> CreateMockCrmConnectorAsync(string tenantId, Domain.TenantConfig? tenantConfig)
    {
        var crmBaseUrl = !string.IsNullOrWhiteSpace(tenantConfig?.CrmBaseUrl)
            ? tenantConfig.CrmBaseUrl
            : _globalCrmConfig.Value.BaseUrl;

        var pipeline = _crmPipelines.GetOrAdd(tenantId, _ => ResiliencePolicies.BuildCrmPipeline());

        var baseHandler = _handlerFactory.CreateHandler("crm-base");
        var pollyHandler = new PollyResilienceHandler(pipeline) { InnerHandler = baseHandler };

        var httpClient = new HttpClient(pollyHandler)
        {
            BaseAddress = new Uri(crmBaseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");

        var refitClient = RestService.For<ICrmApiClient>(httpClient);
        return new MockCrmConnector(refitClient);
    }

    private async Task<ICrmConnector> CreateHansaCrmConnectorAsync(string tenantId, Domain.TenantConfig? tenantConfig)
    {
        var baseUrl = !string.IsNullOrWhiteSpace(tenantConfig?.CrmBaseUrl)
            ? tenantConfig.CrmBaseUrl
            : _hansaCrmConfig.Value.BaseUrl;

        var pipeline = _crmPipelines.GetOrAdd(tenantId, _ => ResiliencePolicies.BuildCrmPipeline());

        var baseHandler = _handlerFactory.CreateHandler("crm-base");
        var pollyHandler = new PollyResilienceHandler(pipeline) { InnerHandler = baseHandler };

        // Separate HttpClient for auth (no Polly pipeline needed; token is cached)
        var authBaseHandler = _handlerFactory.CreateHandler("crm-base");
        var authHttpClient = new HttpClient(authBaseHandler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        var apiHttpClient = new HttpClient(pollyHandler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        var authLogger = _loggerFactory.CreateLogger<HansaCrmAuthService>();
        var authService = new HansaCrmAuthService(
            authHttpClient,
            _hansaCrmConfig,
            _cache,
            authLogger);

        var clientLogger = _loggerFactory.CreateLogger<HansaCrmClient>();
        var client = new HansaCrmClient(apiHttpClient, authService, _hansaCrmConfig.Value, tenantId, clientLogger);

        var defaults = new HansaCrmDefaults
        {
            Organization = _hansaCrmConfig.Value.DefaultOrganization,
            SalesSectors = _hansaCrmConfig.Value.DefaultSalesSectors,
            SalesChannels = _hansaCrmConfig.Value.DefaultSalesChannels,
            SalesOffices = _hansaCrmConfig.Value.DefaultSalesOffices,
            Warehouses = _hansaCrmConfig.Value.DefaultWarehouses
        };

        return new HansaCrmConnector(client, defaults);
    }

    private async Task<Domain.TenantConfig?> GetTenantConfigAsync(string tenantId)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<TenantConfigRepository>();
            return await repo.GetByIdAsync(tenantId);
        }
        catch (Exception ex)
        {
            var logger = _loggerFactory.CreateLogger<TenantClientFactory>();
            logger.LogWarning(ex, "Failed to resolve TenantConfig for {TenantId}, using global fallback", tenantId);
            return null;
        }
    }
}
