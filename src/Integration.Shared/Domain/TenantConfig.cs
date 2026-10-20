using Integration.Shared.Connectors;

namespace Integration.Shared.Domain;

/// <summary>
/// Configuration per tenant. In a future phase each tenant will reside
/// in its own PostgreSQL schema; for now they are stored in the public
/// table with the SAP HANA connection string encrypted at rest.
/// </summary>
public class TenantConfig
{
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ApiKeyHash { get; set; } = string.Empty; // SHA-256 del API Key
    public string SapServiceLayerUrl { get; set; } = string.Empty;
    public string SapCompanyDb { get; set; } = string.Empty;
    public string SapUserName { get; set; } = string.Empty;
    public string SapPasswordEncrypted { get; set; } = string.Empty;
    public string CrmBaseUrl { get; set; } = string.Empty;
    public string CrmApiKeyEncrypted { get; set; } = string.Empty;
    public CrmConnectorType CrmConnectorType { get; set; } = CrmConnectorType.Mock;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }

    // Email notification settings (per tenant)
    public bool EmailEnabled { get; set; }
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public string SmtpUserName { get; set; } = string.Empty;
    public string SmtpPasswordEncrypted { get; set; } = string.Empty;
    public string EmailFromAddress { get; set; } = string.Empty;
}
