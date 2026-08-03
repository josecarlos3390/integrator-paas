using Integration.Shared.Domain;
using Integration.Shared.Dtos;

namespace Integration.Shared.Connectors;

/// <summary>
/// Abstraction over any external CRM system.
/// Implementations are isolated per connector (HansaCRM, Mock, etc.).
/// </summary>
public interface ICrmConnector
{
    Task<CrmApiResponse<object>> CreateInvoiceAsync(CrmInvoicePayload payload, CancellationToken ct = default);
    Task<CrmApiResponse<object>> CreateCustomerAsync(CrmCustomerPayload payload, CancellationToken ct = default);
    Task<CrmApiResponse<object>> CreateVendorAsync(CrmCustomerPayload payload, CancellationToken ct = default);
    Task<CrmApiResponse<object>> SyncPriceListBatchAsync(PriceListChangedPayload payload, CancellationToken ct = default);
    Task<CrmApiResponse<object>> SyncPriceListHeaderAsync(PriceListHeaderPayload payload, CancellationToken ct = default);
}
