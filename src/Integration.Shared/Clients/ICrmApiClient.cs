using Integration.Shared.Domain;
using Integration.Shared.Dtos;
using Refit;

namespace Integration.Shared.Clients;

/// <summary>
/// Refit client for the external CRM REST API.
/// </summary>
public interface ICrmApiClient
{
    /// <summary>
    /// Creates or updates an invoice in the CRM.
    /// 409 Conflict is treated as idempotency (already exists).
    /// </summary>
    [Post("/api/mock/crm/invoices")]
    Task<ApiResponse<object>> CreateInvoiceAsync([Body] CrmInvoicePayload payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends the result of an order processed asynchronously.
    /// </summary>
    [Post("/api/mock/crm/callbacks/order-result")]
    Task<ApiResponse<object>> SendOrderResultAsync([Body] CrmOrderResult payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a customer in the CRM.
    /// 409 Conflict is treated as customer already existing.
    /// </summary>
    [Post("/api/mock/crm/customers")]
    Task<ApiResponse<object>> CreateCustomerAsync([Body] CrmCustomerPayload payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a vendor in the CRM.
    /// </summary>
    [Post("/api/mock/crm/vendors")]
    Task<ApiResponse<object>> CreateVendorAsync([Body] CrmCustomerPayload payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes a batch of price list changes in the CRM.
    /// </summary>
    [Post("/api/mock/crm/price-lists/batch-update")]
    Task<ApiResponse<object>> SyncPriceListBatchAsync([Body] PriceListChangedPayload payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes the header (metadata) of a price list in the CRM.
    /// </summary>
    [Post("/api/mock/crm/price-lists/header-update")]
    Task<ApiResponse<object>> SyncPriceListHeaderAsync([Body] PriceListHeaderPayload payload, CancellationToken cancellationToken = default);
}

public class CrmOrderResult
{
    public string CrmOrderId { get; set; } = string.Empty;
    public string? SapDocEntry { get; set; }
    public string? SapDocNum { get; set; }
    public string Status { get; set; } = string.Empty; // created | error
    public string? ErrorMessage { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
}
