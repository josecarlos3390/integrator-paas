namespace Integration.Shared.Configuration;

public class SapConfig
{
    public string ServiceLayerUrl { get; set; } = string.Empty;
    public string CompanyDB { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool ValidateCertificates { get; set; } = true;
}

public class HanaConfig
{
    public string ConnectionString { get; set; } = string.Empty;
}

public class PostgresConfig
{
    public string ConnectionString { get; set; } = string.Empty;
}

public class CrmConfig
{
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public bool ValidateCertificates { get; set; } = true;
}

public class OutboxConfig
{
    public int PollingSeconds { get; set; } = 5;
    private int _batchSize = 10;
    public int BatchSize
    {
        get => _batchSize;
        set => _batchSize = value >= 1 ? value : 1;
    }
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Priority by SAP document type (ObjectType). Higher number = higher priority.
    /// If not in the dictionary, uses 0 (default priority).
    /// </summary>
    public Dictionary<string, int> EventPriority { get; set; } = new()
    {
        ["2"] = 10,   // BusinessPartners
        ["4"] = 8,    // Items
        ["13"] = 5,   // Invoices
        ["17"] = 3    // Orders
    };

    /// <summary>
    /// Delay in milliseconds between processing each event.
    /// Useful to avoid overwhelming the external CRM. 0 = no delay.
    /// </summary>
    public int RateLimitDelayMs { get; set; } = 0;
}

public class TenantsConfig
{
    public string DefaultTenantId { get; set; } = string.Empty;
}

public class AlertingConfig
{
    public bool Enabled { get; set; } = true;
    public string WebhookUrl { get; set; } = string.Empty;
    public int DeadLetterThreshold { get; set; } = 5;
    public int ErrorRateThreshold { get; set; } = 10;
    public int ErrorRateWindowMinutes { get; set; } = 15;
    public int CheckIntervalMinutes { get; set; } = 5;
}

public class IdempotencyConfig
{
    public bool Enabled { get; set; } = true;
    public int TtlDays { get; set; } = 30;
}
