using System.Text.Json;
using FluentValidation;
using Integration.Shared.Configuration;
using Integration.Shared.Domain;
using Integration.Shared.Dtos;
using Integration.Shared.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Integration.Api.Controllers;

/// <summary>
/// Data Ingestor — receives integration payloads from any external platform,
/// stores them durably, and queues them for asynchronous processing.
/// </summary>
[ApiController]
[Route("api/ingest")]
public class IngestionController : ControllerBase
{
    private readonly IValidator<IngestionPayload> _validator;
    private readonly IntegrationRequestRepository _requestRepo;
    private readonly TenantConfigRepository _tenantRepo;
    private readonly IOptions<TenantsConfig> _tenantsConfig;
    private readonly ILogger<IngestionController> _logger;

    public IngestionController(
        IValidator<IngestionPayload> validator,
        IntegrationRequestRepository requestRepo,
        TenantConfigRepository tenantRepo,
        IOptions<TenantsConfig> tenantsConfig,
        ILogger<IngestionController> logger)
    {
        _validator = validator;
        _requestRepo = requestRepo;
        _tenantRepo = tenantRepo;
        _tenantsConfig = tenantsConfig;
        _logger = logger;
    }

    /// <summary>
    /// Receives a generic integration payload.
    /// Always returns 202 Accepted; processing is asynchronous.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Ingest([FromBody] IngestionPayload payload, CancellationToken ct)
    {
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
        var authenticatedTenantId = HttpContext.Items["TenantId"]?.ToString();
        var tenantId = authenticatedTenantId ?? _tenantsConfig.Value.DefaultTenantId;

        // 1. Validation
        var validation = await _validator.ValidateAsync(payload, ct);
        if (!validation.IsValid)
        {
            return BadRequest(new { Errors = validation.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }) });
        }

        // 2. Tenant override from payload context (if present and valid)
        if (!string.IsNullOrWhiteSpace(payload.Entry?.Context?.TenantId))
        {
            var contextTenantId = payload.Entry.Context.TenantId;

            // Security: when authenticated by API key, payload tenant must match authenticated tenant.
            if (authenticatedTenantId != null &&
                !string.Equals(contextTenantId, authenticatedTenantId, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Cross-tenant ingestion attempt: authenticated tenant {AuthTenantId}, payload tenant {PayloadTenantId}",
                    authenticatedTenantId, contextTenantId);
                return Forbid();
            }

            var contextTenant = await _tenantRepo.GetByIdAsync(contextTenantId, ct);
            if (contextTenant == null || !contextTenant.IsActive)
            {
                return Unauthorized(new { Error = "Invalid or inactive tenant" });
            }

            tenantId = contextTenant.TenantId;
        }

        // 3. Build durable request record
        var request = new IntegrationRequest
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CorrelationId = correlationId,
            SourceSystem = payload.Entry?.Metadata?.SourceSystem ?? "unknown",
            TargetSystem = payload.Entry?.Metadata?.TargetSystem ?? "unknown",
            EntityType = payload.Object,
            Operation = InferOperation(payload.Object),
            ExternalId = payload.Entry?.Id ?? string.Empty,
            Payload = JsonSerializer.Serialize(payload),
            CallbackUrl = ExtractCallbackUrl(payload),
            Status = "received",
            Priority = 0,
            ReceivedAt = DateTime.UtcNow
        };

        await _requestRepo.CreateAsync(request, ct);

        _logger.LogInformation(
            "Ingestion request {RequestId} received for tenant {TenantId}. Entity={EntityType}, Source={SourceSystem}, Target={TargetSystem}",
            request.Id, tenantId, request.EntityType, request.SourceSystem, request.TargetSystem);

        return Accepted(new
        {
            RequestId = request.Id,
            CorrelationId = correlationId,
            Status = "received"
        });
    }

    private static string InferOperation(string objectType)
    {
        // Default to create; future: inspect Messages for operation hints
        return "create";
    }

    private static string? ExtractCallbackUrl(IngestionPayload payload)
    {
        // Future: allow callers to pass a callback URL in metadata or context
        return null;
    }
}
