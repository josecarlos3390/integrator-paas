using FluentValidation;
using Integration.Shared.Clients;
using Integration.Shared.Configuration;
using Integration.Shared.Domain;
using Integration.Shared.Dtos;
using Integration.Shared.Exceptions;
using Integration.Shared.Mappers;
using Integration.Shared.Messages;
using Integration.Shared.Repositories;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Integration.Api.Controllers;

/// <summary>
/// Receives orders from the external CRM. Supports synchronous
/// (waits for SAP response) or asynchronous (202 Accepted + callback).
/// </summary>
[ApiController]
[Route("api/crm")]
public class CrmWebhookController : ControllerBase
{
    private readonly ITenantClientFactory _clientFactory;
    private readonly IValidator<CrmOrderPayload> _validator;
    private readonly IntegrationLogRepository _logRepo;
    private readonly IBus _bus;
    private readonly IOptions<TenantsConfig> _tenantsConfig;
    private readonly ILogger<CrmWebhookController> _logger;

    public CrmWebhookController(
        ITenantClientFactory clientFactory,
        IValidator<CrmOrderPayload> validator,
        IntegrationLogRepository logRepo,
        IBus bus,
        IOptions<TenantsConfig> tenantsConfig,
        ILogger<CrmWebhookController> logger)
    {
        _clientFactory = clientFactory;
        _validator = validator;
        _logRepo = logRepo;
        _bus = bus;
        _tenantsConfig = tenantsConfig;
        _logger = logger;
    }

    /// <summary>
    /// Receives an order from the CRM.
    /// If the payload includes CallbackUrl, processing is asynchronous.
    /// </summary>
    [HttpPost("orders")]
    public async Task<IActionResult> CreateOrder([FromBody] CrmOrderPayload payload, CancellationToken ct)
    {
        var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? _tenantsConfig.Value.DefaultTenantId;
        var correlationId = HttpContext.Items["CorrelationId"]?.ToString() ?? Guid.NewGuid().ToString();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 1. Validation
        var validation = await _validator.ValidateAsync(payload, ct);
        if (!validation.IsValid)
        {
            return BadRequest(new { Errors = validation.Errors.Select(e => new { e.PropertyName, e.ErrorMessage }) });
        }

        // 2. Async mode
        if (!string.IsNullOrWhiteSpace(payload.CallbackUrl))
        {
            var message = new CreateOrderMessage
            {
                TenantId = tenantId,
                CorrelationId = correlationId,
                Payload = payload,
                CallbackUrl = payload.CallbackUrl
            };

            await _bus.Publish(message, ct);

            _logger.LogInformation("Order {CrmOrderId} queued for async processing. CorrelationId={CorrelationId}",
                payload.CrmOrderId, correlationId);

            return Accepted(new { CorrelationId = correlationId, Status = "queued" });
        }

        // 3. Sync mode
        try
        {
            var sapClient = await _clientFactory.GetSapClientAsync(tenantId);

            // Idempotency: check if it already exists in SAP
            var existing = await sapClient.GetOrderByNumAtCardAsync(payload.CrmOrderId, ct);
            if (existing.HasValue)
            {
                _logger.LogInformation("Order {CrmOrderId} already exists in SAP with DocEntry {DocEntry}",
                    payload.CrmOrderId, existing.Value);

                sw.Stop();
                await _logRepo.AddAsync(new IntegrationLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    CorrelationId = correlationId,
                    Direction = IntegrationDirection.CrmToSap,
                    EventType = "SalesOrderCreated",
                    ExternalId = payload.CrmOrderId,
                    SapDocEntry = existing.Value.ToString(),
                    Status = "success",
                    DurationMs = sw.ElapsedMilliseconds,
                    CreatedAt = DateTime.UtcNow
                }, ct);

                return Ok(new { SapDocEntry = existing.Value, SapDocNum = 0, Status = "already_exists" });
            }

            // Create in SAP
            var sapPayload = OrderMapper.ToSapPayload(payload);
            var (docEntry, docNum) = await sapClient.CreateOrderAsync(sapPayload, ct);

            sw.Stop();
            await _logRepo.AddAsync(new IntegrationLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CorrelationId = correlationId,
                Direction = IntegrationDirection.CrmToSap,
                EventType = "SalesOrderCreated",
                ExternalId = payload.CrmOrderId,
                SapDocEntry = docEntry.ToString(),
                Status = "success",
                DurationMs = sw.ElapsedMilliseconds,
                CreatedAt = DateTime.UtcNow
            }, ct);

            return Ok(new { SapDocEntry = docEntry, SapDocNum = docNum, Status = "created" });
        }
        catch (SapIntegrationException sapEx) when (sapEx.IsBusinessError)
        {
            sw.Stop();
            await _logRepo.AddAsync(new IntegrationLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CorrelationId = correlationId,
                Direction = IntegrationDirection.CrmToSap,
                EventType = "SalesOrderCreated",
                ExternalId = payload.CrmOrderId,
                Status = "business_error",
                ErrorMessage = sapEx.Message,
                DurationMs = sw.ElapsedMilliseconds,
                CreatedAt = DateTime.UtcNow
            }, ct);

            return UnprocessableEntity(new { Error = sapEx.Message, Code = sapEx.SapErrorCode });
        }
        catch (Exception ex)
        {
            sw.Stop();
            await _logRepo.AddAsync(new IntegrationLog
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CorrelationId = correlationId,
                Direction = IntegrationDirection.CrmToSap,
                EventType = "SalesOrderCreated",
                ExternalId = payload.CrmOrderId,
                Status = "error",
                ErrorMessage = ex.Message,
                DurationMs = sw.ElapsedMilliseconds,
                CreatedAt = DateTime.UtcNow
            }, ct);

            _logger.LogError(ex, "Error creating order {CrmOrderId} in SAP", payload.CrmOrderId);
            return StatusCode(502, new { Error = "Upstream integration error", Details = ex.Message });
        }
    }
}
