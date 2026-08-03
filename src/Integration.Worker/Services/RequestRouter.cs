using System.Text.Json;
using Integration.Shared.Clients;
using Integration.Shared.Connectors;
using Integration.Shared.Domain;
using Integration.Shared.Dtos;
using Integration.Shared.Mappers;
using Microsoft.Extensions.Logging;

namespace Integration.Worker.Services;

/// <summary>
/// Default implementation of the request router.
/// Maps entity types to the appropriate outbound connector.
/// </summary>
public class RequestRouter : IRequestRouter
{
    private readonly ITenantClientFactory _clientFactory;
    private readonly ILogger<RequestRouter> _logger;

    public RequestRouter(
        ITenantClientFactory clientFactory,
        ILogger<RequestRouter> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
    }

    public bool CanRoute(string entityType, string targetSystem)
    {
        var key = $"{entityType}:{targetSystem}".ToLowerInvariant();
        return key switch
        {
            "account:crm" => true,
            "vendor:crm" => true,
            "invoice:crm" => true,
            "price_list:crm" => true,
            "price_list_header:crm" => true,
            "order:erp" => true,
            _ => false
        };
    }

    public async Task<string?> RouteAsync(IntegrationRequest request, CancellationToken ct = default)
    {
        var key = $"{request.EntityType}:{request.TargetSystem}".ToLowerInvariant();

        _logger.LogInformation(
            "Routing request {RequestId}: {EntityType} → {TargetSystem}",
            request.Id, request.EntityType, request.TargetSystem);

        return key switch
        {
            "account:crm" => await RouteAccountToCrmAsync(request, ct),
            "vendor:crm" => await RouteVendorToCrmAsync(request, ct),
            "invoice:crm" => await RouteInvoiceToCrmAsync(request, ct),
            "price_list:crm" => await RoutePriceListToCrmAsync(request, ct),
            "price_list_header:crm" => await RoutePriceListHeaderToCrmAsync(request, ct),
            "order:erp" => await RouteOrderToErpAsync(request, ct),
            _ => throw new NotSupportedException(
                $"Route not supported: {request.EntityType} → {request.TargetSystem}")
        };
    }

    private async Task<string?> RouteAccountToCrmAsync(IntegrationRequest request, CancellationToken ct)
    {
        var connector = await _clientFactory.GetCrmConnectorAsync(request.TenantId);
        var payload = ExtractMessage<CrmCustomerPayload>(request);
        var response = await connector.CreateCustomerAsync(payload, ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"CRM rejected customer: {response.ErrorMessage}");

        return payload.ExternalId;
    }

    private async Task<string?> RouteVendorToCrmAsync(IntegrationRequest request, CancellationToken ct)
    {
        var connector = await _clientFactory.GetCrmConnectorAsync(request.TenantId);
        var payload = ExtractMessage<CrmCustomerPayload>(request);
        var response = await connector.CreateVendorAsync(payload, ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"CRM rejected vendor: {response.ErrorMessage}");

        return payload.ExternalId;
    }

    private async Task<string?> RouteInvoiceToCrmAsync(IntegrationRequest request, CancellationToken ct)
    {
        var connector = await _clientFactory.GetCrmConnectorAsync(request.TenantId);
        var payload = ExtractMessage<CrmInvoicePayload>(request);
        var response = await connector.CreateInvoiceAsync(payload, ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"CRM rejected invoice: {response.ErrorMessage}");

        return payload.ExternalId;
    }

    private async Task<string?> RoutePriceListToCrmAsync(IntegrationRequest request, CancellationToken ct)
    {
        var connector = await _clientFactory.GetCrmConnectorAsync(request.TenantId);
        var payload = ExtractMessage<PriceListChangedPayload>(request);
        var response = await connector.SyncPriceListBatchAsync(payload, ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"CRM rejected price list: {response.ErrorMessage}");

        return payload.ListNum.ToString();
    }

    private async Task<string?> RoutePriceListHeaderToCrmAsync(IntegrationRequest request, CancellationToken ct)
    {
        var connector = await _clientFactory.GetCrmConnectorAsync(request.TenantId);
        var payload = ExtractMessage<PriceListHeaderPayload>(request);
        var response = await connector.SyncPriceListHeaderAsync(payload, ct);

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"CRM rejected price list header: {response.ErrorMessage}");

        return payload.ListNum.ToString();
    }

    private async Task<string?> RouteOrderToErpAsync(IntegrationRequest request, CancellationToken ct)
    {
        var sapClient = await _clientFactory.GetSapClientAsync(request.TenantId);
        var payload = ExtractMessage<CrmOrderPayload>(request);

        // Idempotency: check if already exists
        var existing = await sapClient.GetOrderByNumAtCardAsync(payload.CrmOrderId, ct);
        if (existing.HasValue)
        {
            _logger.LogInformation("Order {CrmOrderId} already exists in SAP", payload.CrmOrderId);
            return existing.Value.ToString();
        }

        var sapPayload = OrderMapper.ToSapPayload(payload);
        var (docEntry, docNum) = await sapClient.CreateOrderAsync(sapPayload, ct);
        return docEntry.ToString();
    }

    private static T ExtractMessage<T>(IntegrationRequest request) where T : class, new()
    {
        using var doc = JsonDocument.Parse(request.Payload);
        var root = doc.RootElement;

        if (root.TryGetProperty("entry", out var entry) &&
            entry.TryGetProperty("messages", out var messages) &&
            messages.GetArrayLength() > 0)
        {
            var firstMessage = messages[0];
            var deserialized = firstMessage.Deserialize<T>();
            if (deserialized != null)
                return deserialized;
        }

        var fallback = JsonSerializer.Deserialize<T>(request.Payload);
        if (fallback != null)
            return fallback;

        return new T();
    }
}
