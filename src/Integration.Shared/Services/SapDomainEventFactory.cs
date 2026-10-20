using System.Globalization;
using System.Text.Json;
using Integration.Shared.Clients;
using Integration.Shared.Messages;
using Integration.Shared.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace Integration.Shared.Services;

/// <summary>
/// Resolves SAP keys and enriches HANA outbox events before publishing.
/// Consumers receive a stable domain contract instead of raw SAP payloads.
/// </summary>
public class SapDomainEventFactory : ISapDomainEventFactory
{
    private readonly ITenantClientFactory _clientFactory;
    private readonly TenantConfigRepository _tenantRepo;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SapDomainEventFactory> _logger;

    public SapDomainEventFactory(
        ITenantClientFactory clientFactory,
        TenantConfigRepository tenantRepo,
        IMemoryCache cache,
        ILogger<SapDomainEventFactory> logger)
    {
        _clientFactory = clientFactory;
        _tenantRepo = tenantRepo;
        _cache = cache;
        _logger = logger;
    }

    public async Task<SapDomainEvent?> BuildAsync(
        HanaOutboxEvent evt, CancellationToken ct = default)
    {
        try
        {
            var tenant = await _tenantRepo.GetByIdIncludingInactiveAsync(evt.TenantId, ct);
            var domain = new SapDomainEvent
            {
                EventId = evt.Id,
                TenantId = evt.TenantId,
                CompanyDb = tenant?.SapCompanyDb ?? string.Empty,
                EventType = evt.EventType,
                ObjectType = evt.ObjectType,
                AggregateId = evt.AggregateId,
                SchemaVersion = 1,
                OccurredAt = DateTime.SpecifyKind(evt.OccurredAt, DateTimeKind.Utc)
            };

            var (itemCode, cardCode) = ExtractKeys(evt);
            var client = await _clientFactory.GetSapClientAsync(evt.TenantId);

            switch (evt.ObjectType)
            {
                case "4":
                    if (string.IsNullOrWhiteSpace(itemCode)) return null;
                    domain.AggregateId = itemCode;
                    domain.Data = await CachedAsync(
                        $"item:{evt.TenantId}:{itemCode}",
                        TimeSpan.FromSeconds(15),
                        async () => MapItem(await client.GetItemAsync(itemCode, ct)));
                    break;

                case "7":
                    if (string.IsNullOrWhiteSpace(itemCode)) return null;
                    domain.AggregateId = itemCode;
                    domain.Data = MapPricing(
                        itemCode,
                        cardCode,
                        await client.GetSpecialPricesAsync(itemCode, ct));
                    break;

                case "2":
                    if (string.IsNullOrWhiteSpace(cardCode)) return null;
                    domain.AggregateId = cardCode;
                    domain.Data = await CachedAsync(
                        $"bp:{evt.TenantId}:{cardCode}",
                        TimeSpan.FromSeconds(30),
                        async () => await client.GetBusinessPartnerRawAsync(cardCode, ct));
                    break;

                case "13":
                    if (!int.TryParse(evt.AggregateId, out var invoiceEntry))
                        return null;
                    domain.Data = await client.GetInvoiceAsync(invoiceEntry, ct);
                    break;

                default:
                    domain.Data = new { aggregateId = evt.AggregateId };
                    break;
            }

            return domain;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "No se pudo normalizar el evento SAP {EventId} ({ObjectType})",
                evt.Id, evt.ObjectType);
            return null;
        }
    }

    private async Task<T> CachedAsync<T>(
        string key, TimeSpan ttl, Func<Task<T>> loader)
    {
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
            return cached;
        var value = await loader();
        _cache.Set(key, value, ttl);
        return value;
    }

    private static (string ItemCode, string CardCode) ExtractKeys(HanaOutboxEvent evt)
    {
        var item = string.Empty;
        var card = string.Empty;
        if (!string.IsNullOrWhiteSpace(evt.Payload))
        {
            try
            {
                using var doc = JsonDocument.Parse(evt.Payload);
                var root = doc.RootElement;
                var columns = (root.TryGetProperty("keyColumns", out var c)
                    ? c.GetString()
                    : root.TryGetProperty("KeyColumns", out var pc) ? pc.GetString() : null)
                    ?.Split(new[] { ',', '\t' }, StringSplitOptions.TrimEntries) ?? Array.Empty<string>();
                var values = (root.TryGetProperty("keyValues", out var v)
                    ? v.GetString()
                    : root.TryGetProperty("KeyValues", out var pv) ? pv.GetString() : null)
                    ?.Split(new[] { '\t', ',' }, StringSplitOptions.TrimEntries)
                    ?? Array.Empty<string>();
                for (var i = 0; i < Math.Min(columns.Length, values.Length); i++)
                {
                    if (columns[i].Equals("ItemCode", StringComparison.OrdinalIgnoreCase))
                        item = values[i].Trim('\'', '"');
                    if (columns[i].Equals("CardCode", StringComparison.OrdinalIgnoreCase))
                        card = values[i].Trim('\'', '"');
                }
            }
            catch (JsonException)
            {
                // Fall back to aggregate id.
            }
        }

        if (string.IsNullOrWhiteSpace(item))
        {
            var parts = evt.AggregateId.Split(new[] { '\t', ',' }, StringSplitOptions.TrimEntries);
            item = parts.FirstOrDefault(p => !p.StartsWith('*') && !p.StartsWith("C", StringComparison.OrdinalIgnoreCase)
                && p.Length > 3) ?? evt.AggregateId;
            card = parts.FirstOrDefault(p => p.StartsWith('*') || p.StartsWith("C", StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(card) && evt.AggregateId.Contains('\t'))
            card = evt.AggregateId.Split('\t').LastOrDefault()?.Trim() ?? string.Empty;

        return (item.Trim().ToUpperInvariant(), card.Trim());
    }

    private static object MapItem(JsonElement item)
    {
        decimal price = 0;
        if (item.TryGetProperty("ItemPrices", out var prices))
        {
            foreach (var entry in prices.EnumerateArray())
            {
                if (entry.TryGetProperty("PriceList", out var list) && list.GetInt32() == 1)
                {
                    price = entry.GetProperty("Price").GetDecimal();
                    break;
                }
            }
        }

        var stores = new Dictionary<string, int>();
        if (item.TryGetProperty("ItemWarehouseInfoCollection", out var warehouses))
        {
            foreach (var warehouse in warehouses.EnumerateArray())
            {
                var code = warehouse.GetProperty("WarehouseCode").GetString();
                if (string.IsNullOrWhiteSpace(code)) continue;
                var inStock = warehouse.TryGetProperty("InStock", out var s) ? s.GetDecimal() : 0m;
                var committed = warehouse.TryGetProperty("Committed", out var c) ? c.GetDecimal() : 0m;
                stores[code] = (int)(inStock - committed);
            }
        }

        return new
        {
            sku = item.GetProperty("ItemCode").GetString(),
            name = item.GetProperty("ItemName").GetString(),
            price,
            stock = item.TryGetProperty("QuantityOnStock", out var q) ? (int)q.GetDecimal() : 0,
            currency = "BOB",
            stock_by_store = stores
        };
    }

    private static object MapPricing(string itemCode, string cardCode, JsonElement data)
    {
        var periods = new List<object>();
        if (data.TryGetProperty("value", out var rows))
        {
            foreach (var row in rows.EnumerateArray())
            {
                var rowCard = row.TryGetProperty("CardCode", out var cc) ? cc.GetString() : null;
                if (rowCard is not null && rowCard != "*1" && rowCard != "*") continue;
                if (!row.TryGetProperty("SpecialPriceDataAreas", out var areas)) continue;
                foreach (var area in areas.EnumerateArray())
                {
                    var priceList = area.TryGetProperty("PriceListNo", out var pl)
                        ? pl.GetInt32()
                        : row.TryGetProperty("PriceListNum", out var rp) ? rp.GetInt32() : 1;
                    var special = area.TryGetProperty("SpecialPrice", out var sp) ? sp.GetDecimal() : 0m;
                    var tiers = new List<object>();
                    if (area.TryGetProperty("SpecialPriceQuantityAreas", out var tierRows))
                    {
                        foreach (var tier in tierRows.EnumerateArray())
                        {
                            var qty = tier.TryGetProperty("Quantity", out var q) ? q.GetDecimal() : 0m;
                            var tierPrice = tier.TryGetProperty("SpecialPrice", out var tp) ? tp.GetDecimal() : 0m;
                            if (qty <= 0 || tierPrice <= 0) continue;
                            tiers.Add(new
                            {
                                quantity = (int)qty,
                                price = tierPrice,
                                discount_percent = tier.TryGetProperty("Discountin", out var d) ? d.GetDecimal() : 0m
                            });
                        }
                    }
                    if (special <= 0 && tiers.Count == 0) continue;
                    periods.Add(new
                    {
                        price_list = priceList,
                        valid_from = area.TryGetProperty("DateFrom", out var df)
                            ? df.GetString()?[..10]
                            : null,
                        valid_to = area.TryGetProperty("Dateto", out var dt)
                            ? dt.GetString()?[..10]
                            : null,
                        price = special > 0 ? special : (decimal?)null,
                        discount_percent = area.TryGetProperty("Discount", out var disc) ? disc.GetDecimal() : 0m,
                        tiers
                    });
                }
            }
        }

        return new
        {
            sku = itemCode,
            card_code = string.IsNullOrWhiteSpace(cardCode) ? "*1" : cardCode,
            periods
        };
    }
}
