using Integration.Shared.Clients;
using Integration.Shared.Configuration;
using Integration.Shared.Dtos;
using Integration.Shared.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Integration.Api.Controllers;

/// <summary>
/// Write facade over SAP B1 for downstream consumers such as the WhatsApp bot.
/// Requires the tenant X-Api-Key header (ApiKeyMiddleware).
/// </summary>
[ApiController]
[Route("api/sap")]
public class SapWriteController : ControllerBase
{
    private readonly ITenantClientFactory _clientFactory;
    private readonly ILogger<SapWriteController> _logger;
    private readonly string _defaultTenantId;

    public SapWriteController(
        ITenantClientFactory clientFactory,
        IOptions<TenantsConfig> tenantsConfig,
        ILogger<SapWriteController> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
        _defaultTenantId = tenantsConfig.Value.DefaultTenantId;
    }

    /// <summary>
    /// Creates a customer (business partner) in SAP. Idempotent by CardCode.
    /// </summary>
    [HttpPost("business-partners")]
    public async Task<IActionResult> CreateBusinessPartner(
        [FromBody] SapBusinessPartnerPayload payload, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(payload.CardCode) || string.IsNullOrWhiteSpace(payload.CardName))
        {
            return BadRequest(new { Error = "CardCode y CardName son obligatorios" });
        }

        try
        {
            var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? _defaultTenantId;
            var client = await _clientFactory.GetSapClientAsync(tenantId);
            var bp = await client.CreateBusinessPartnerAsync(payload, ct);
            var cardCode = bp.TryGetProperty("CardCode", out var code)
                ? code.GetString()
                : payload.CardCode;

            return Ok(new { CardCode = cardCode });
        }
        catch (SapIntegrationException ex)
        {
            _logger.LogWarning(ex, "SAP rechazó el socio {CardCode}", payload.CardCode);
            return UnprocessableEntity(new { Error = ex.Message, Code = ex.SapErrorCode });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creando socio {CardCode}", payload.CardCode);
            return StatusCode(502, new { Error = "Upstream integration error", Details = ex.Message });
        }
    }
}
