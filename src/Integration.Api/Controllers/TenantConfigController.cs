using Integration.Shared.Clients;
using Integration.Shared.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Integration.Api.Controllers;

/// <summary>
/// Self-service configuration endpoints for an authenticated tenant.
/// The tenant is identified by its API key (X-Api-Key) via ApiKeyMiddleware;
/// the tenantId always comes from HttpContext.Items, never from the request.
/// </summary>
[ApiController]
[Route("api/tenant")]
public class TenantConfigController : ControllerBase
{
    private readonly TenantConfigRepository _tenantRepo;
    private readonly TenantSettingsRepository _settingsRepo;
    private readonly ITenantClientFactory _clientFactory;
    private readonly ILogger<TenantConfigController> _logger;

    public TenantConfigController(
        TenantConfigRepository tenantRepo,
        TenantSettingsRepository settingsRepo,
        ITenantClientFactory clientFactory,
        ILogger<TenantConfigController> logger)
    {
        _tenantRepo = tenantRepo;
        _settingsRepo = settingsRepo;
        _clientFactory = clientFactory;
        _logger = logger;
    }

    private string CurrentTenantId =>
        HttpContext.Items["TenantId"]?.ToString() ?? string.Empty;

    /// <summary>
    /// Returns the tenant's own configuration without secret values.
    /// </summary>
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig(CancellationToken ct)
    {
        var tenantId = CurrentTenantId;
        var config = await _tenantRepo.GetByIdAsync(tenantId, ct);
        if (config is null)
            return NotFound(new { Message = "Tenant configuration not found." });

        return Ok(new
        {
            config.TenantId,
            config.Name,
            config.SapServiceLayerUrl,
            config.SapCompanyDb,
            config.SapUserName,
            HasSapPassword = !string.IsNullOrEmpty(config.SapPasswordEncrypted),
            config.CrmBaseUrl,
            HasCrmApiKey = !string.IsNullOrEmpty(config.CrmApiKeyEncrypted),
            config.EmailEnabled,
            config.SmtpHost,
            config.SmtpPort,
            config.SmtpUserName,
            HasSmtpPassword = !string.IsNullOrEmpty(config.SmtpPasswordEncrypted),
            config.EmailFromAddress
        });
    }

    /// <summary>
    /// Updates the tenant's own configuration. Empty password fields keep the
    /// current value. TenantId, Name, IsActive and CrmConnectorType are
    /// master-only and cannot be changed here.
    /// </summary>
    [HttpPut("config")]
    public async Task<IActionResult> UpdateConfig([FromBody] UpdateTenantConfigRequest request, CancellationToken ct)
    {
        var tenantId = CurrentTenantId;
        var config = await _tenantRepo.GetByIdAsync(tenantId, ct);
        if (config is null)
            return NotFound(new { Message = "Tenant configuration not found." });

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
        _logger.LogInformation("Tenant {TenantId} updated its own configuration", tenantId);

        return Ok(new { Message = "Configuration updated." });
    }

    /// <summary>Lists the tenant's generic key-value settings.</summary>
    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var settings = await _settingsRepo.GetByTenantAsync(CurrentTenantId, ct);
        return Ok(settings.Select(s => new { s.SettingKey, s.SettingValue, s.UpdatedAt }));
    }

    /// <summary>Creates or updates one of the tenant's generic key-value settings.</summary>
    [HttpPut("settings/{settingKey}")]
    public async Task<IActionResult> SetSetting(string settingKey, [FromBody] SetSettingRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settingKey) || settingKey.Length > 128)
            return BadRequest(new { Message = "SettingKey is required and must be at most 128 characters." });
        if (request.SettingValue is null)
            return BadRequest(new { Message = "SettingValue is required." });

        await _settingsRepo.SetAsync(CurrentTenantId, settingKey.Trim(), request.SettingValue, ct);
        return Ok(new { SettingKey = settingKey.Trim(), SettingValue = request.SettingValue });
    }

    /// <summary>Deletes one of the tenant's generic key-value settings.</summary>
    [HttpDelete("settings/{settingKey}")]
    public async Task<IActionResult> DeleteSetting(string settingKey, CancellationToken ct)
    {
        await _settingsRepo.DeleteAsync(CurrentTenantId, settingKey, ct);
        return Ok(new { SettingKey = settingKey });
    }

    public class UpdateTenantConfigRequest
    {
        public string? SapServiceLayerUrl { get; set; }
        public string? SapCompanyDb { get; set; }
        public string? SapUserName { get; set; }
        public string? SapPassword { get; set; }
        public string? CrmBaseUrl { get; set; }
        public string? CrmApiKey { get; set; }
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
}
