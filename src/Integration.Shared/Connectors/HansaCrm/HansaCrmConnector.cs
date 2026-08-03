using Integration.Shared.Connectors.HansaCrm.Dtos;
using Integration.Shared.Domain;
using Integration.Shared.Dtos;

namespace Integration.Shared.Connectors.HansaCrm;

/// <summary>
/// HansaCRM connector implementation.
/// All payloads are sent to the single integration endpoint.
/// The "object" field in the wrapper identifies the entity type.
/// </summary>
public class HansaCrmConnector : ICrmConnector
{
    private readonly HansaCrmClient _client;
    private readonly HansaCrmDefaults _defaults;

    public HansaCrmConnector(HansaCrmClient client, HansaCrmDefaults defaults)
    {
        _client = client;
        _defaults = defaults;
    }

    public async Task<CrmApiResponse<object>> CreateInvoiceAsync(CrmInvoicePayload payload, CancellationToken ct = default)
    {
        var wrapper = HansaCrmMapper.MapReceivable(payload, _defaults);
        return await _client.SendAsync<HansaCrmPayloadWrapper, object>(wrapper, ct);
    }

    public async Task<CrmApiResponse<object>> CreateCustomerAsync(CrmCustomerPayload payload, CancellationToken ct = default)
    {
        var wrapper = payload.Type?.ToLowerInvariant() switch
        {
            "csupplier" => HansaCrmMapper.MapVendor(payload, _defaults),
            _ => HansaCrmMapper.MapAccount(payload, _defaults)
        };

        return await _client.SendAsync<HansaCrmPayloadWrapper, object>(wrapper, ct);
    }

    public async Task<CrmApiResponse<object>> CreateVendorAsync(CrmCustomerPayload payload, CancellationToken ct = default)
    {
        payload.Type = "csupplier";
        return await CreateCustomerAsync(payload, ct);
    }

    public async Task<CrmApiResponse<object>> SyncPriceListBatchAsync(PriceListChangedPayload payload, CancellationToken ct = default)
    {
        // TODO: Implement HansaCRM price-list mapper when the payload format is provided.
        var wrapper = HansaCrmMapper.MapPriceList(payload, _defaults);
        return await _client.SendAsync<HansaCrmPayloadWrapper, object>(wrapper, ct);
    }

    public async Task<CrmApiResponse<object>> SyncPriceListHeaderAsync(PriceListHeaderPayload payload, CancellationToken ct = default)
    {
        // TODO: Implement HansaCRM price-list header mapper when the payload format is provided.
        var wrapper = HansaCrmMapper.MapPriceListHeader(payload, _defaults);
        return await _client.SendAsync<HansaCrmPayloadWrapper, object>(wrapper, ct);
    }

    // ------------------------------------------------------------------
    // Batch methods (HansaCRM-specific, not part of ICrmConnector)
    // ------------------------------------------------------------------

    public async Task<CrmApiResponse<object>> CreateCustomerBatchAsync(
        List<CrmCustomerPayload> payloads,
        int totalRecords,
        int batchRecords,
        int batchQuantity,
        int batchNumber,
        CancellationToken ct = default)
    {
        // Separate accounts from vendors
        var accounts = payloads.Where(p => p.Type?.ToLowerInvariant() != "csupplier").ToList();
        var vendors = payloads.Where(p => p.Type?.ToLowerInvariant() == "csupplier").ToList();

        // HansaCRM requires a single object type per payload.
        // If the batch is mixed, we send accounts first (most common).
        // In practice, batches should be homogeneous.
        var wrapper = accounts.Count > 0
            ? HansaCrmMapper.MapAccountBatch(accounts, _defaults, totalRecords, batchRecords, batchQuantity, batchNumber)
            : HansaCrmMapper.MapVendorBatch(vendors, _defaults, totalRecords, batchRecords, batchQuantity, batchNumber);

        return await _client.SendAsync<HansaCrmPayloadWrapper, object>(wrapper, ct);
    }

    public async Task<CrmApiResponse<object>> CreateInvoiceBatchAsync(
        List<CrmInvoicePayload> payloads,
        int totalRecords,
        int batchRecords,
        int batchQuantity,
        int batchNumber,
        CancellationToken ct = default)
    {
        var wrapper = HansaCrmMapper.MapReceivableBatch(payloads, _defaults, totalRecords, batchRecords, batchQuantity, batchNumber);
        return await _client.SendAsync<HansaCrmPayloadWrapper, object>(wrapper, ct);
    }
}
