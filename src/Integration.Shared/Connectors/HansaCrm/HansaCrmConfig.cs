namespace Integration.Shared.Connectors.HansaCrm;

/// <summary>
/// Configuration for HansaCRM OAuth2 and API endpoints.
/// </summary>
public class HansaCrmConfig
{
    /// <summary>
    /// Base URL up to the SYNC segment, e.g.
    /// https://api.hansacrm.com/crm/v2/hbm/10000265/QAS/SYNC/
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Relative path of the single integration endpoint.
    /// Default: "integration/api"
    /// </summary>
    public string IntegrationEndpoint { get; set; } = "data-ingestion-services";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string AuthenticatedUserId { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string ProvisionKey { get; set; } = string.Empty;

    /// <summary>
    /// Seconds to cache the access token before re-authenticating.
    /// Default: 7000 (HansaCRM tokens expire in 7200s).
    /// </summary>
    public int TokenCacheSeconds { get; set; } = 7000;
    public bool ValidateCertificates { get; set; } = true;

    /// <summary>
    /// Number of records per batch payload sent to HansaCRM.
    /// Default: 25. Must be >= 1.
    /// </summary>
    private int _batchSize = 25;
    public int BatchSize
    {
        get => _batchSize;
        set => _batchSize = value >= 1 ? value : 1;
    }

    // ------------------------------------------------------------------------
    // Default values for HansaCRM-specific fields (not present in SAP B1).
    // These are injected into every vendor-integrator payload.
    // ------------------------------------------------------------------------
    public string DefaultOrganization { get; set; } = string.Empty;
    public List<string> DefaultSalesSectors { get; set; } = new();
    public List<string> DefaultSalesChannels { get; set; } = new();
    public List<string> DefaultSalesOffices { get; set; } = new();
    public List<string> DefaultWarehouses { get; set; } = new();
}
