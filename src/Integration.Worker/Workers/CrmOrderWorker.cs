using Integration.Shared.Clients;
using Integration.Shared.Domain;
using Integration.Shared.Dtos;
using Integration.Shared.Exceptions;
using Integration.Shared.Mappers;
using Integration.Shared.Messages;
using Integration.Shared.Repositories;
using Integration.Shared.Services;
using MassTransit;

namespace Integration.Worker.Workers;

/// <summary>
/// MassTransit consumer that processes asynchronously queued orders.
/// Runs when the API receives an order from the CRM with CallbackUrl and opts
/// for the asynchronous path (202 Accepted).
/// </summary>
public class CrmOrderWorker : IConsumer<CreateOrderMessage>
{
    private readonly ITenantClientFactory _clientFactory;
    private readonly IntegrationLogRepository _logRepo;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IIdempotencyService _idempotencyService;
    private readonly ILogger<CrmOrderWorker> _logger;

    public CrmOrderWorker(
        ITenantClientFactory clientFactory,
        IntegrationLogRepository logRepo,
        IHttpClientFactory httpClientFactory,
        IIdempotencyService idempotencyService,
        ILogger<CrmOrderWorker> logger)
    {
        _clientFactory = clientFactory;
        _logRepo = logRepo;
        _httpClientFactory = httpClientFactory;
        _idempotencyService = idempotencyService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<CreateOrderMessage> context)
    {
        var message = context.Message;
        var correlationId = message.CorrelationId;
        var ct = context.CancellationToken;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation("Processing async order {CrmOrderId} for tenant {TenantId}",
            message.Payload.CrmOrderId, message.TenantId);

        var sapClient = await _clientFactory.GetSapClientAsync(message.TenantId);

        try
        {
            var idempotencyResult = await _idempotencyService.TryProcessAsync(
                message.TenantId, "SalesOrderCreated", message.Payload.CrmOrderId,
                async () =>
                {
                    // 1. Check if it already exists in SAP by NumAtCard (double-check)
                    var existing = await sapClient.GetOrderByNumAtCardAsync(message.Payload.CrmOrderId);
                    if (existing.HasValue)
                    {
                        _logger.LogInformation("Order {CrmOrderId} already exists in SAP with DocEntry {DocEntry}",
                            message.Payload.CrmOrderId, existing.Value);
                        return;
                    }

                    // 2. Transform and create in SAP
                    var sapPayload = OrderMapper.ToSapPayload(message.Payload);
                    var (docEntry, docNum) = await sapClient.CreateOrderAsync(sapPayload);

                    _logger.LogInformation("Order {CrmOrderId} created in SAP with DocEntry {DocEntry}, DocNum {DocNum}",
                        message.Payload.CrmOrderId, docEntry, docNum);

                    // 3. Audit
                    sw.Stop();
                    await _logRepo.AddAsync(new IntegrationLog
                    {
                        Id = Guid.NewGuid(),
                        TenantId = message.TenantId,
                        CorrelationId = correlationId,
                        Direction = IntegrationDirection.CrmToSap,
                        EventType = "SalesOrderCreated",
                        ExternalId = message.Payload.CrmOrderId,
                        SapDocEntry = docEntry.ToString(),
                        Status = "success",
                        DurationMs = sw.ElapsedMilliseconds,
                        CreatedAt = DateTime.UtcNow
                    });

                    // 4. Notify CRM if there is a callback
                    await NotifyCrmAsync(message, docEntry.ToString(), docNum.ToString(), true, null, ct);
                });

            if (idempotencyResult == IdempotencyResult.AlreadyProcessed)
            {
                _logger.LogInformation("Order {CrmOrderId} was already processed (idempotency hit)", message.Payload.CrmOrderId);
                return;
            }
        }
        catch (SapIntegrationException sapEx)
        {
            sw.Stop();
            _logger.LogError(sapEx, "SAP business error processing order {CrmOrderId}", message.Payload.CrmOrderId);

            await _logRepo.AddAsync(new IntegrationLog
            {
                Id = Guid.NewGuid(),
                TenantId = message.TenantId,
                CorrelationId = correlationId,
                Direction = IntegrationDirection.CrmToSap,
                EventType = "SalesOrderCreated",
                ExternalId = message.Payload.CrmOrderId,
                Status = "error",
                ErrorMessage = sapEx.Message,
                DurationMs = sw.ElapsedMilliseconds,
                CreatedAt = DateTime.UtcNow
            });

            await NotifyCrmAsync(message, null, null, false, sapEx.Message, ct);
            throw; // Re-lanzar para que MassTransit maneje el retry/dead-letter
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Unexpected error processing order {CrmOrderId}", message.Payload.CrmOrderId);

            await _logRepo.AddAsync(new IntegrationLog
            {
                Id = Guid.NewGuid(),
                TenantId = message.TenantId,
                CorrelationId = correlationId,
                Direction = IntegrationDirection.CrmToSap,
                EventType = "SalesOrderCreated",
                ExternalId = message.Payload.CrmOrderId,
                Status = "error",
                ErrorMessage = ex.Message,
                DurationMs = sw.ElapsedMilliseconds,
                CreatedAt = DateTime.UtcNow
            });

            await NotifyCrmAsync(message, null, null, false, ex.Message, ct);
            throw;
        }
    }

    private async Task NotifyCrmAsync(CreateOrderMessage message, string? docEntry, string? docNum, bool success, string? error, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(message.CallbackUrl)) return;

        try
        {
            var result = new CrmOrderResult
            {
                CrmOrderId = message.Payload.CrmOrderId,
                SapDocEntry = docEntry,
                SapDocNum = docNum,
                Status = success ? "created" : "error",
                ErrorMessage = error,
                CorrelationId = message.CorrelationId
            };

            using var client = _httpClientFactory.CreateClient();
            var json = System.Text.Json.JsonSerializer.Serialize(result);
            using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
            var response = await client.PostAsync(message.CallbackUrl, content, ct);
            _logger.LogInformation("CRM callback {Url} responded {StatusCode}", message.CallbackUrl, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to notify CRM callback for order {CrmOrderId}", message.Payload.CrmOrderId);
        }
    }
}
