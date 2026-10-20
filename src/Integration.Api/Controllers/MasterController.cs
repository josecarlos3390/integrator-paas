using System.Security.Cryptography;
using System.Text;
using Integration.Shared.Clients;
using Integration.Shared.Connectors;
using Integration.Shared.Domain;
using Integration.Shared.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Integration.Api.Controllers;

/// <summary>
/// Master administration endpoints (tenant CRUD, licensing and per-tenant
/// configuration). Protected by the X-Admin-Key header (see ApiKeyMiddleware).
/// </summary>
[ApiController]
[Route("api/master")]
public class MasterController : ControllerBase
{
    private readonly TenantConfigRepository _tenantRepo;
    private readonly TenantFeatureFlagRepository _featureRepo;
    private readonly TenantSettingsRepository _settingsRepo;
    private readonly ITenantClientFactory _clientFactory;
    private readonly ILogger<MasterController> _logger;

    public MasterController(
        TenantConfigRepository tenantRepo,
        TenantFeatureFlagRepository featureRepo,
        TenantSettingsRepository settingsRepo,
        ITenantClientFactory clientFactory,
        ILogger<MasterController> logger)
    {
        _tenantRepo = tenantRepo;
        _featureRepo = featureRepo;
        _settingsRepo = settingsRepo;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Lists all tenants, including inactive ones, without exposing secrets.
    /// </summary>
    [HttpGet("tenants")]
    public async Task<IActionResult> GetAllTenants(CancellationToken ct)
    {
        var tenants = await _tenantRepo.GetAllIncludingInactiveAsync(ct);
        return Ok(tenants.Select(ToSafeConfig));
    }

    /// <summary>
    /// Creates a tenant and returns the plaintext API key exactly once.
    /// </summary>
    [HttpPost("tenants")]
    public async Task<IActionResult> CreateTenant([FromBody] CreateTenantRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId) || request.TenantId.Length > 64)
            return BadRequest(new { Message = "TenantId is required and must be at most 64 characters." });
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { Message = "Name is required." });

        var existing = await _tenantRepo.GetByIdIncludingInactiveAsync(request.TenantId, ct);
        if (existing is not null)
            return Conflict(new { Message = $"Tenant '{request.TenantId}' already exists." });

        var apiKey = GenerateApiKey();
        var config = new TenantConfig
        {
            TenantId = request.TenantId.Trim(),
            Name = request.Name.Trim(),
            ApiKeyHash = HashApiKey(apiKey),
            SapServiceLayerUrl = request.SapServiceLayerUrl ?? string.Empty,
            SapCompanyDb = request.SapCompanyDb ?? string.Empty,
            SapUserName = request.SapUserName ?? string.Empty,
            SapPasswordEncrypted = request.SapPassword ?? string.Empty,
            CrmBaseUrl = request.CrmBaseUrl ?? string.Empty,
            CrmApiKeyEncrypted = request.CrmApiKey ?? string.Empty,
            CrmConnectorType = request.CrmConnectorType ?? CrmConnectorType.Mock,
            EmailEnabled = request.EmailEnabled,
            SmtpHost = request.SmtpHost ?? string.Empty,
            SmtpPort = request.SmtpPort ?? 587,
            SmtpUserName = request.SmtpUserName ?? string.Empty,
            SmtpPasswordEncrypted = request.SmtpPassword ?? string.Empty,
            EmailFromAddress = request.EmailFromAddress ?? string.Empty,
            IsActive = request.IsActive,
            CreatedAt = DateTime.UtcNow
        };

        await _tenantRepo.CreateAsync(config, ct);
        _logger.LogInformation("Tenant {TenantId} created by master administration", config.TenantId);

        return Created($"/api/master/tenants/{config.TenantId}", new
        {
            Tenant = ToSafeConfig(config),
            ApiKey = apiKey
        });
    }

    /// <summary>
    /// Updates a tenant's configuration. Empty password fields keep the current value.
    /// </summary>
    [HttpPut("tenants/{tenantId}")]
    public async Task<IActionResult> UpdateTenant(string tenantId, [FromBody] UpdateTenantRequest request, CancellationToken ct)
    {
        var config = await _tenantRepo.GetByIdIncludingInactiveAsync(tenantId, ct);
        if (config is null)
            return NotFound(new { Message = $"Tenant '{tenantId}' not found." });

        if (!string.IsNullOrWhiteSpace(request.Name))
            config.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.SapServiceLayerUrl))
            config.SapServiceLayerUrl = request.SapServiceLayerUrl.Trim();
        if (!string.IsNullOrWhiteSpace(request.SapCompanyDb))
            config.SapCompanyDb = request.SapCompanyDb.Trim();
        if (!string.IsNullOrWhiteSpace(request.SapUserName))
            config.SapUserName = request.SapUserName.Trim();
        if (!string.IsNullOrWhiteSpace(request.SapPassword))
            config.SapPasswordEncrypted = request.SapPassword;
        if (!string.IsNullOrWhiteSpace(request.CrmBaseUrl))
            config.CrmBaseUrl = request.CrmBaseUrl.Trim();
        if (!string.IsNullOrWhiteSpace(request.CrmApiKey))
            config.CrmApiKeyEncrypted = request.CrmApiKey;
        if (request.CrmConnectorType is not null)
            config.CrmConnectorType = request.CrmConnectorType.Value;
        if (request.EmailEnabled is not null)
            config.EmailEnabled = request.EmailEnabled.Value;
        if (!string.IsNullOrWhiteSpace(request.SmtpHost))
            config.SmtpHost = request.SmtpHost.Trim();
        if (request.SmtpPort is not null)
            config.SmtpPort = request.SmtpPort.Value;
        if (!string.IsNullOrWhiteSpace(request.SmtpUserName))
            config.SmtpUserName = request.SmtpUserName.Trim();
        if (!string.IsNullOrWhiteSpace(request.SmtpPassword))
            config.SmtpPasswordEncrypted = request.SmtpPassword;
        if (!string.IsNullOrWhiteSpace(request.EmailFromAddress))
            config.EmailFromAddress = request.EmailFromAddress.Trim();

        await _tenantRepo.UpdateAsync(config, ct);
        await _clientFactory.EvictTenantAsync(tenantId);
        _logger.LogInformation("Tenant {TenantId} configuration updated by master administration", tenantId);

        return Ok(ToSafeConfig(config));
    }

    /// <summary>Activates a tenant (licensing).</summary>
    [HttpPost("tenants/{tenantId}/activate")]
    public async Task<IActionResult> ActivateTenant(string tenantId, CancellationToken ct)
    {
        return await SetActiveAsync(tenantId, true, ct);
    }

    /// <summary>Deactivates a tenant (licensing). Stops processing and API access.</summary>
    [HttpPost("tenants/{tenantId}/deactivate")]
    public async Task<IActionResult> DeactivateTenant(string tenantId, CancellationToken ct)
    {
        return await SetActiveAsync(tenantId, false, ct);
    }

    private async Task<IActionResult> SetActiveAsync(string tenantId, bool isActive, CancellationToken ct)
    {
        var config = await _tenantRepo.GetByIdIncludingInactiveAsync(tenantId, ct);
        if (config is null)
            return NotFound(new { Message = $"Tenant '{tenantId}' not found." });

        await _tenantRepo.SetActiveAsync(tenantId, isActive, ct);
        await _clientFactory.EvictTenantAsync(tenantId);
        _logger.LogInformation("Tenant {TenantId} set to {Status} by master administration", tenantId, isActive ? "active" : "inactive");

        return Ok(new { TenantId = tenantId, IsActive = isActive });
    }

    /// <summary>Regenerates the tenant API key; the new key is returned once.</summary>
    [HttpPost("tenants/{tenantId}/api-key")]
    public async Task<IActionResult> RegenerateApiKey(string tenantId, CancellationToken ct)
    {
        var config = await _tenantRepo.GetByIdIncludingInactiveAsync(tenantId, ct);
        if (config is null)
            return NotFound(new { Message = $"Tenant '{tenantId}' not found." });

        var apiKey = GenerateApiKey();
        await _tenantRepo.UpdateApiKeyHashAsync(tenantId, HashApiKey(apiKey), ct);
        _logger.LogInformation("API key regenerated for tenant {TenantId}", tenantId);

        return Ok(new { TenantId = tenantId, ApiKey = apiKey });
    }

    /// <summary>Lists the generic key-value settings of a tenant.</summary>
    [HttpGet("tenants/{tenantId}/settings")]
    public async Task<IActionResult> GetSettings(string tenantId, CancellationToken ct)
    {
        var config = await _tenantRepo.GetByIdIncludingInactiveAsync(tenantId, ct);
        if (config is null)
            return NotFound(new { Message = $"Tenant '{tenantId}' not found." });

        var settings = await _settingsRepo.GetByTenantAsync(tenantId, ct);
        return Ok(settings.Select(s => new { s.SettingKey, s.SettingValue, s.UpdatedAt }));
    }

    /// <summary>Creates or updates a generic key-value setting of a tenant.</summary>
    [HttpPut("tenants/{tenantId}/settings/{settingKey}")]
    public async Task<IActionResult> SetSetting(string tenantId, string settingKey, [FromBody] SetSettingRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settingKey) || settingKey.Length > 128)
            return BadRequest(new { Message = "SettingKey is required and must be at most 128 characters." });
        if (request.SettingValue is null)
            return BadRequest(new { Message = "SettingValue is required." });

        var config = await _tenantRepo.GetByIdIncludingInactiveAsync(tenantId, ct);
        if (config is null)
            return NotFound(new { Message = $"Tenant '{tenantId}' not found." });

        await _settingsRepo.SetAsync(tenantId, settingKey.Trim(), request.SettingValue, ct);
        return Ok(new { TenantId = tenantId, SettingKey = settingKey.Trim(), SettingValue = request.SettingValue });
    }

    /// <summary>Deletes a generic key-value setting of a tenant.</summary>
    [HttpDelete("tenants/{tenantId}/settings/{settingKey}")]
    public async Task<IActionResult> DeleteSetting(string tenantId, string settingKey, CancellationToken ct)
    {
        var config = await _tenantRepo.GetByIdIncludingInactiveAsync(tenantId, ct);
        if (config is null)
            return NotFound(new { Message = $"Tenant '{tenantId}' not found." });

        await _settingsRepo.DeleteAsync(tenantId, settingKey, ct);
        return Ok(new { TenantId = tenantId, SettingKey = settingKey });
    }

    /// <summary>Lists the feature flags of a tenant.</summary>
    [HttpGet("tenants/{tenantId}/features")]
    public async Task<IActionResult> GetFeatureFlags(string tenantId, CancellationToken ct)
    {
        var config = await _tenantRepo.GetByIdIncludingInactiveAsync(tenantId, ct);
        if (config is null)
            return NotFound(new { Message = $"Tenant '{tenantId}' not found." });

        var flags = await _featureRepo.GetByTenantAsync(tenantId, ct);
        return Ok(flags);
    }

    /// <summary>Enables or disables a feature flag for a tenant.</summary>
    [HttpPost("tenants/{tenantId}/features/{featureKey}")]
    public async Task<IActionResult> SetFeatureFlag(string tenantId, string featureKey, [FromBody] SetFeatureFlagRequest request, CancellationToken ct)
    {
        var config = await _tenantRepo.GetByIdIncludingInactiveAsync(tenantId, ct);
        if (config is null)
            return NotFound(new { Message = $"Tenant '{tenantId}' not found." });

        await _featureRepo.SetAsync(tenantId, featureKey, request.IsEnabled, ct);
        _logger.LogInformation("Feature flag {FeatureKey} for tenant {TenantId} set to {Enabled}", featureKey, tenantId, request.IsEnabled);
        return Ok(new { TenantId = tenantId, FeatureKey = featureKey, IsEnabled = request.IsEnabled });
    }

    private static object ToSafeConfig(TenantConfig t) => new
    {
        t.TenantId,
        t.Name,
        t.IsActive,
        t.CreatedAt,
        t.SapServiceLayerUrl,
        t.SapCompanyDb,
        t.SapUserName,
        HasSapPassword = !string.IsNullOrEmpty(t.SapPasswordEncrypted),
        t.CrmBaseUrl,
        HasCrmApiKey = !string.IsNullOrEmpty(t.CrmApiKeyEncrypted),
        t.CrmConnectorType,
        t.EmailEnabled,
        t.SmtpHost,
        t.SmtpPort,
        t.SmtpUserName,
        HasSmtpPassword = !string.IsNullOrEmpty(t.SmtpPasswordEncrypted),
        t.EmailFromAddress
    };

    private static string GenerateApiKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string HashApiKey(string apiKey) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));

    public class CreateTenantRequest
    {
        public string TenantId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? SapServiceLayerUrl { get; set; }
        public string? SapCompanyDb { get; set; }
        public string? SapUserName { get; set; }
        public string? SapPassword { get; set; }
        public string? CrmBaseUrl { get; set; }
        public string? CrmApiKey { get; set; }
        public CrmConnectorType? CrmConnectorType { get; set; }
        public bool EmailEnabled { get; set; }
        public string? SmtpHost { get; set; }
        public int? SmtpPort { get; set; }
        public string? SmtpUserName { get; set; }
        public string? SmtpPassword { get; set; }
        public string? EmailFromAddress { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class UpdateTenantRequest
    {
        public string? Name { get; set; }
        public string? SapServiceLayerUrl { get; set; }
        public string? SapCompanyDb { get; set; }
        public string? SapUserName { get; set; }
        public string? SapPassword { get; set; }
        public string? CrmBaseUrl { get; set; }
        public string? CrmApiKey { get; set; }
        public CrmConnectorType? CrmConnectorType { get; set; }
        public bool? EmailEnabled { get; set; }
        public string? SmtpHost { get; set; }
        public int? SmtpPort { get; set; }
        public string? SmtpUserName { get; set; }
        public string? SmtpPassword { get; set; }
        public string? EmailFromAddress { get; set; }
    }

    public class SetSettingRequest
    {
        public string SettingValue { get; set; } = string.Empty;
    }

    public class SetFeatureFlagRequest
    {
        public bool IsEnabled { get; set; }
    }
}
