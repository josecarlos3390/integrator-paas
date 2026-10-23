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
    /// <summary>
    /// Default (legacy) connection string. Always polled by the worker.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Additional HANA servers polled by the outbox dispatcher (multi-HANA support).
    /// Events carry their own TENANT_ID, so routing is resolved downstream per event.
    /// </summary>
    public List<HanaConnectionConfig> Connections { get; set; } = new();
}

public class HanaConnectionConfig
{
    public string Name { get; set; } = string.Empty;
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
    /// When true, each HANA outbox event is also published to the integration
    /// event bus (RabbitMQ) for additional consumers such as the chatbot.
    /// Disabled by default so the existing CRM flow is not affected.
    /// </summary>
    public bool PublishEvents { get; set; } = false;

    public string EventExchange { get; set; } = "sap.events";

    /// <summary>
    /// Object types published to downstream consumers. Inventory movements
    /// are intentionally excluded to avoid overloading the bus.
    /// </summary>
    public HashSet<string> PublishObjectTypes { get; set; } = new()
    {
        "2", "4", "7", "13"
    };

    /// <summary>
    /// Tenants the built-in CRM demo consumer subscribes to. Each value becomes
    /// a topic binding {tenant}.{objectType}.* on the crm.sap.events queue.
    /// Real consumers declare their own queues/bindings.
    /// </summary>
    public HashSet<string> CrmQueueTenants { get; set; } = new()
    {
        "RETAIL_QA5"
    };

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

public class AdminConfig
{
    /// <summary>
    /// SHA-256 hex hash of the master administration API key
    /// (X-Admin-Key header) for /api/master endpoints.
    /// </summary>
    public string MasterApiKeyHash { get; set; } = string.Empty;
}

public class TenantCacheConfig
{
    /// <summary>
    /// TTL in seconds for cached per-tenant SAP/CRM clients. After this period
    /// clients are rebuilt, picking up configuration changes without restart.
    /// </summary>
    public int ClientCacheTtlSeconds { get; set; } = 300;
}

/// <summary>
/// VTEX read-only facade configuration (PaasVTEX). Credentials stay in the
/// PaaS; the bot only receives the normalized /api/vtex response.
/// </summary>
public class VtexConfig
{
    public string Account { get; set; } = string.Empty;
    public string AppKey { get; set; } = string.Empty;
    public string AppToken { get; set; } = string.Empty;
    public int SalesChannel { get; set; } = 1;
    public string Currency { get; set; } = "BOB";

    /// <summary>
    /// Maps the store code used by the bot (e.g. 101-1001) to the VTEX
    /// warehouse id returned by the Logistics API.
    /// </summary>
    public Dictionary<string, string> WarehouseMap { get; set; } = new();

    public string BaseUrl =>
        string.IsNullOrWhiteSpace(Account)
            ? string.Empty
            : $"https://{Account}.vtexcommercestable.com.br";

    public string PricingUrl =>
        string.IsNullOrWhiteSpace(Account)
            ? string.Empty
            : $"https://api.vtex.com/{Account}";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Account) &&
        !string.IsNullOrWhiteSpace(AppKey) &&
        !string.IsNullOrWhiteSpace(AppToken);
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

/// <summary>
/// Telegram Bot API settings for operational notifications
/// (e.g. VENDOR_BANK_ALERT anti-fraud alerts).
/// </summary>
public class TelegramConfig
{
    public bool Enabled { get; set; } = false;
    public string BotToken { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
}

public class IdempotencyConfig
{
    public bool Enabled { get; set; } = true;
    public int TtlDays { get; set; } = 30;
}
