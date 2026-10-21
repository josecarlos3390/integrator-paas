using System.Text.Json;
using Integration.Shared.Clients;
using Integration.Shared.Configuration;
using Integration.Shared.Dtos;
using Integration.Shared.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Integration.Api.Controllers;

/// <summary>
/// Read-only SAP facade for downstream consumers such as the WhatsApp bot.
/// Requires the tenant X-Api-Key header (ApiKeyMiddleware).
/// </summary>
[ApiController]
[Route("api/sap")]
public class SapReadController : ControllerBase
{
    private readonly ITenantClientFactory _clientFactory;
    private readonly ILogger<SapReadController> _logger;
    private readonly string _defaultTenantId;

    public SapReadController(
        ITenantClientFactory clientFactory,
        IOptions<TenantsConfig> tenantsConfig,
        ILogger<SapReadController> logger)
    {
        _clientFactory = clientFactory;
        _logger = logger;
        _defaultTenantId = tenantsConfig.Value.DefaultTenantId;
    }

    /// <summary>
    /// Exporta una página del catálogo para la sincronización del bot.
    /// El bot deja de necesitar credenciales de SAP.
    /// </summary>
    [HttpGet("items/export")]
    public async Task<IActionResult> ExportItems(
        [FromQuery] string? after,
        [FromQuery] int limit,
        CancellationToken ct)
    {
        try
        {
            var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? _defaultTenantId;
            var client = await _clientFactory.GetSapClientAsync(tenantId);
            var items = await client.ExportItemsAsync(after, limit <= 0 ? 200 : limit, ct);
            return Ok(items);
        }
        catch (SapIntegrationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message, code = ex.SapErrorCode });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exportando el catálogo");
            return StatusCode(502, new { error = "SAP export failed" });
        }
    }

    /// <summary>
    /// Grupos de artículos y fabricantes, para traducir códigos a nombres.
    /// </summary>
    [HttpGet("catalog-reference")]
    public async Task<IActionResult> CatalogReference(CancellationToken ct)
    {
        try
        {
            var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? _defaultTenantId;
            var client = await _clientFactory.GetSapClientAsync(tenantId);
            var groups = await client.GetReferenceAsync("ItemGroups", "Number,GroupName", ct);
            var manufacturers = await client.GetReferenceAsync(
                "Manufacturers", "Code,ManufacturerName", ct);
            return Ok(new { groups, manufacturers });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo grupos y fabricantes");
            return StatusCode(502, new { error = "SAP reference failed" });
        }
    }

    /// <summary>
    /// Busca artículos por nombre en SAP (para la caché del bot).
    /// </summary>
    [HttpGet("items")]
    public async Task<IActionResult> SearchItems(
        [FromQuery] string q,
        [FromQuery] int limit,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest(new { error = "Falta el parámetro q" });
        }
        var take = limit <= 0 ? 8 : Math.Min(limit, 25);
        try
        {
            var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? _defaultTenantId;
            var client = await _clientFactory.GetSapClientAsync(tenantId);
            var items = await client.SearchItemsAsync(q, take, ct);

            var result = new List<object>();
            if (items.ValueKind != JsonValueKind.Array)
            {
                return Ok(new { query = q, items = result });
            }
            foreach (var item in items.EnumerateArray())
            {
                var price = 0m;
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
                    if (price == 0m && prices.GetArrayLength() > 0)
                    {
                        price = prices[0].GetProperty("Price").GetDecimal();
                    }
                }
                result.Add(new
                {
                    sku = item.GetProperty("ItemCode").GetString(),
                    name = item.GetProperty("ItemName").GetString(),
                    price,
                    stock = item.TryGetProperty("QuantityOnStock", out var qty) ? (int)qty.GetDecimal() : 0,
                    currency = "BOB"
                });
            }
            return Ok(new { query = q, items = result });
        }
        catch (SapIntegrationException ex)
        {
            return UnprocessableEntity(new { error = ex.Message, code = ex.SapErrorCode });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error buscando artículos {Query} en SAP", q);
            return StatusCode(502, new { error = "SAP search failed" });
        }
    }

    [HttpGet("items/{sku}")]
    public async Task<IActionResult> GetItem(string sku, [FromQuery(Name = "stores")] string? storesFilter, CancellationToken ct)
    {
        try
        {
            var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? _defaultTenantId;
            var client = await _clientFactory.GetSapClientAsync(tenantId);
            var item = await client.GetItemAsync(sku, ct);

            var price = 0m;
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
                if (price == 0m && prices.GetArrayLength() > 0)
                {
                    price = prices[0].GetProperty("Price").GetDecimal();
                }
            }

            var stores = new Dictionary<string, int>();
            var wanted = string.IsNullOrWhiteSpace(storesFilter)
                ? null
                : storesFilter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (item.TryGetProperty("ItemWarehouseInfoCollection", out var warehouses))
            {
                foreach (var warehouse in warehouses.EnumerateArray())
                {
                    var code = warehouse.GetProperty("WarehouseCode").GetString();
                    if (string.IsNullOrWhiteSpace(code)) continue;
                    if (wanted is not null && !wanted.Contains(code)) continue;
                    var inStock = warehouse.TryGetProperty("InStock", out var s) ? s.GetDecimal() : 0m;
                    var committed = warehouse.TryGetProperty("Committed", out var c) ? c.GetDecimal() : 0m;
                    stores[code] = (int)(inStock - committed);
                }
            }

            return Ok(new
            {
                sku = item.GetProperty("ItemCode").GetString(),
                name = item.GetProperty("ItemName").GetString(),
                price,
                stock = item.TryGetProperty("QuantityOnStock", out var q) ? (int)q.GetDecimal() : 0,
                currency = item.TryGetProperty("Currency", out var cur) ? cur.GetString() : "BOB",
                stock_by_store = stores
            });
        }
        catch (SapIntegrationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading item {Sku} from SAP", sku);
            return StatusCode(502, new { error = "SAP read failed" });
        }
    }

    [HttpGet("items/{sku}/pricing")]
    public async Task<IActionResult> GetPricing(string sku, CancellationToken ct)
    {
        try
        {
            var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? _defaultTenantId;
            var client = await _clientFactory.GetSapClientAsync(tenantId);
            var data = await client.GetSpecialPricesAsync(sku, ct);

            var periods = new List<object>();
            if (data.TryGetProperty("value", out var rows))
            {
                foreach (var row in rows.EnumerateArray())
                {
                    var cardCode = row.TryGetProperty("CardCode", out var cc) ? cc.GetString() : null;
                    if (cardCode is not null && cardCode != "*1" && cardCode != "*") continue;
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
                                var quantity = tier.TryGetProperty("Quantity", out var q) ? q.GetDecimal() : 0m;
                                var tierPrice = tier.TryGetProperty("SpecialPrice", out var tp) ? tp.GetDecimal() : 0m;
                                if (quantity <= 0 || tierPrice <= 0) continue;
                                tiers.Add(new
                                {
                                    quantity = (int)quantity,
                                    price = tierPrice,
                                    discount_percent = tier.TryGetProperty("Discountin", out var d) ? d.GetDecimal() : 0m
                                });
                            }
                        }
                        if (special <= 0 && tiers.Count == 0) continue;
                        periods.Add(new
                        {
                            price_list = priceList,
                            valid_from = area.TryGetProperty("DateFrom", out var df) ? df.GetString() : null,
                            valid_to = area.TryGetProperty("Dateto", out var dt) ? dt.GetString() : null,
                            price = special > 0 ? special : (decimal?)null,
                            discount_percent = area.TryGetProperty("Discount", out var disc) ? disc.GetDecimal() : 0m,
                            tiers
                        });
                    }
                }
            }

            return Ok(new { sku, periods });
        }
        catch (SapIntegrationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading pricing for {Sku}", sku);
            return StatusCode(502, new { error = "SAP pricing read failed" });
        }
    }

    [HttpGet("invoices/{docEntry:int}")]
    public async Task<IActionResult> GetInvoice(int docEntry, CancellationToken ct)
    {
        try
        {
            var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? _defaultTenantId;
            var client = await _clientFactory.GetSapClientAsync(tenantId);
            var invoice = await client.GetInvoiceAsync(docEntry, ct);
            return Ok(invoice);
        }
        catch (SapIntegrationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading invoice {DocEntry} from SAP", docEntry);
            return StatusCode(502, new { error = "SAP invoice read failed" });
        }
    }

    [HttpGet("customers/{cardCode}/purchases")]
    public async Task<IActionResult> GetCustomerPurchases(
        string cardCode,
        [FromQuery] int top = 20,
        [FromQuery] int skip = 0,
        CancellationToken ct = default)
    {
        try
        {
            var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? _defaultTenantId;
            var client = await _clientFactory.GetSapClientAsync(tenantId);
            var data = await client.GetCustomerInvoicesAsync(cardCode, Math.Clamp(top, 1, 100), Math.Max(skip, 0), ct);
            return Ok(data);
        }
        catch (SapIntegrationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading purchases for {CardCode}", cardCode);
            return StatusCode(502, new { error = "SAP purchases read failed" });
        }
    }

    [HttpGet("business-partners/{cardCode}")]
    public async Task<IActionResult> GetBusinessPartner(string cardCode, CancellationToken ct)
    {
        try
        {
            var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? _defaultTenantId;
            var client = await _clientFactory.GetSapClientAsync(tenantId);
            var partner = await client.GetBusinessPartnerRawAsync(cardCode, ct);
            return Ok(partner);
        }
        catch (SapIntegrationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading business partner {CardCode} from SAP", cardCode);
            return StatusCode(502, new { error = "SAP read failed" });
        }
    }
}
