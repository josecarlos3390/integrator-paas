using Integration.Shared.Clients;
using Integration.Shared.Domain;
using Integration.Shared.Dtos;
using System.Net;

namespace Integration.Shared.Connectors;

/// <summary>
/// Adapter that wraps the existing Refit mock client into the generic ICrmConnector abstraction.
/// Keeps the mock functional while the rest of the codebase moves to ICrmConnector.
/// </summary>
public class MockCrmConnector : ICrmConnector
{
    private readonly ICrmApiClient _refitClient;

    public MockCrmConnector(ICrmApiClient refitClient)
    {
        _refitClient = refitClient;
    }

    public async Task<CrmApiResponse<object>> CreateInvoiceAsync(CrmInvoicePayload payload, CancellationToken ct = default)
    {
        var response = await _refitClient.CreateInvoiceAsync(payload, ct);
        return Map(response);
    }

    public async Task<CrmApiResponse<object>> CreateCustomerAsync(CrmCustomerPayload payload, CancellationToken ct = default)
    {
        var response = await _refitClient.CreateCustomerAsync(payload, ct);
        return Map(response);
    }

    public async Task<CrmApiResponse<object>> CreateVendorAsync(CrmCustomerPayload payload, CancellationToken ct = default)
    {
        var response = await _refitClient.CreateVendorAsync(payload, ct);
        return Map(response);
    }

    public async Task<CrmApiResponse<object>> SyncPriceListBatchAsync(PriceListChangedPayload payload, CancellationToken ct = default)
    {
        var response = await _refitClient.SyncPriceListBatchAsync(payload, ct);
        return Map(response);
    }

    public async Task<CrmApiResponse<object>> SyncPriceListHeaderAsync(PriceListHeaderPayload payload, CancellationToken ct = default)
    {
        var response = await _refitClient.SyncPriceListHeaderAsync(payload, ct);
        return Map(response);
    }

    private static CrmApiResponse<T> Map<T>(Refit.ApiResponse<T> refitResponse)
    {
        return new CrmApiResponse<T>
        {
            StatusCode = refitResponse.StatusCode,
            Content = refitResponse.Content,
            ErrorMessage = refitResponse.Error?.Content
        };
    }
}
