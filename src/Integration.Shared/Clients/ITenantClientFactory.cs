using Integration.Shared.Connectors;

namespace Integration.Shared.Clients;

/// <summary>
/// Factory that creates/returns configured HTTP clients for a specific tenant.
/// Caches instances per tenantId to reuse SAP sessions and CRM connections.
/// </summary>
public interface ITenantClientFactory
{
    /// <summary>
    /// Gets the SAP Service Layer client for the given tenant.
    /// </summary>
    Task<ServiceLayerClient> GetSapClientAsync(string tenantId);

    /// <summary>
    /// Gets the CRM connector for the given tenant.
    /// The concrete implementation depends on the tenant's CrmConnectorType config.
    /// </summary>
    Task<ICrmConnector> GetCrmConnectorAsync(string tenantId);

    /// <summary>
    /// Drops the cached clients/pipelines for a tenant so the next access
    /// rebuilds them from the latest tenant_config. Called after a config
    /// update through the administration interface.
    /// </summary>
    Task EvictTenantAsync(string tenantId);
}
