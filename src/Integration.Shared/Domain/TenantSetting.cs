namespace Integration.Shared.Domain;

/// <summary>
/// Generic per-tenant key-value setting. Allows administrators to
/// parameterize behavior per tenant without schema migrations.
/// </summary>
public class TenantSetting
{
    public string TenantId { get; set; } = string.Empty;
    public string SettingKey { get; set; } = string.Empty;
    public string SettingValue { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}
