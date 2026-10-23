using System.Globalization;
using System.Text.Json;
using Integration.Shared.Clients;
using Integration.Shared.Configuration;
using Integration.Shared.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Integration.Api.Controllers;

/// <summary>
/// PaasVTEX: read-only VTEX facade for the WhatsApp bot.
///
/// The bot keeps the same contract it already uses for /api/sap, but the data
/// comes from VTEX. No POST/PUT/PATCH/DELETE endpoints are exposed.
/// </summary>
[ApiController]
[Route("api/vtex")]
public class VtexReadController : ControllerBase
{
    private readonly VtexClient _client;
    private readonly VtexConfig _config;
    private readonly ILogger<VtexReadController> _logger;

    public VtexReadController(
        VtexClient client,
        IOptions<VtexConfig> config,
        ILogger<VtexReadController> logger)
    {
        _client = client;
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>
    /// Página del catálogo para repoblar la base del bot.
    /// after = offset de productos VTEX; limit = productos por página (máx. 50).
    /// </summary>
    [HttpGet("items/export")]
    public async Task<IActionResult> ExportItems(
        [FromQuery] string? after,
        [FromQuery(Name = "brand")] string? brandId,
        [FromQuery(Name = "price")] string? priceRange,
        [FromQuery] int limit,
        CancellationToken ct)
    {
        var offset = int.TryParse(after, out var parsed) ? Math.Max(parsed, 0) : 0;
        var take = limit <= 0 ? 50 : Math.Clamp(limit, 1, 50);

        try
        {
            var products = await _client.GetExportPageAsync(
                offset, take, brandId, priceRange, ct);
            var items = new List<object>();
            if (products.ValueKind != JsonValueKind.Array)
            {
                return Ok(items);
            }

            foreach (var product in products.EnumerateArray())
            {
                var productName = Text(product, "productName");
                var description = Text(product, "description")
                    ?? Text(product, "metaTagDescription")
                    ?? productName;
                var brand = Text(product, "brand");
                var categories = StringArray(product, "categories");
                var category = categories
                    .Select(value => value.Trim('/').Split('/').LastOrDefault())
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
                var productId = Text(product, "productId");

                if (!product.TryGetProperty("items", out var productItems) ||
                    productItems.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var item in productItems.EnumerateArray())
                {
                    var refId = ReferenceId(item);
                    var itemId = Text(item, "itemId");
                    var sku = !string.IsNullOrWhiteSpace(refId) ? refId! : itemId;
                    if (string.IsNullOrWhiteSpace(sku))
                    {
                        continue;
                    }

                    var offer = FirstOffer(item);
                    var price = DecimalValue(offer, "Price");
                    var listPrice = DecimalValue(offer, "ListPrice");
                    var stock = IntValue(offer, "AvailableQuantity");
                    var imageUrl = FirstImage(item);
                    var nameComplete = Text(item, "nameComplete")
                        ?? Text(item, "name")
                        ?? productName;

                    items.Add(new
                    {
                        sku,
                        name = nameComplete,
                        name_complete = nameComplete,
                        product_name = productName,
                        description,
                        category,
                        brand,
                        barcode = Text(item, "ean"),
                        price,
                        currency = _client.Currency,
                        stock,
                        image_url = imageUrl,
                        images = ImageUrls(item),
                        attributes = new Dictionary<string, object?>
                        {
                            ["source"] = "vtex",
                            ["vtex_product_id"] = productId,
                            ["vtex_sku_id"] = itemId,
                            ["vtex_ref_id"] = refId,
                            ["vtex_ean"] = Text(item, "ean"),
                            ["list_price"] = listPrice,
                            ["price_valid_until"] = Text(offer, "PriceValidUntil"),
                            ["sales_channel"] = _client.SalesChannel
                        }
                    });
                }
            }

            return Ok(items);
        }
        catch (VtexIntegrationException ex)
        {
            return UpstreamError(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error exportando catálogo VTEX");
            return StatusCode(502, new { error = "VTEX export failed" });
        }
    }

    [HttpGet("brands")]
    public async Task<IActionResult> GetBrands(CancellationToken ct)
    {
        try
        {
            var data = await _client.GetBrandsAsync(ct);
            var brands = new List<object>();
            if (data.ValueKind != JsonValueKind.Array)
            {
                return Ok(brands);
            }

            foreach (var brand in data.EnumerateArray())
            {
                brands.Add(new
                {
                    id = Text(brand, "id"),
                    name = Text(brand, "name")
                });
            }

            return Ok(brands);
        }
        catch (VtexIntegrationException ex)
        {
            return UpstreamError(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo marcas VTEX");
            return StatusCode(502, new { error = "VTEX brands read failed" });
        }
    }

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
            var products = await _client.SearchProductsAsync(q, 0, take - 1, ct);
            var items = new List<object>();

            if (products.ValueKind == JsonValueKind.Array)
            {
                foreach (var product in products.EnumerateArray())
                {
                    if (items.Count >= take ||
                        !product.TryGetProperty("items", out var productItems) ||
                        productItems.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var productName = Text(product, "productName");
                    var description = Text(product, "description")
                        ?? Text(product, "metaTagDescription")
                        ?? productName;
                    var brand = Text(product, "brand");
                    var categories = StringArray(product, "categories");
                    var category = CleanCategory(categories.FirstOrDefault());
                    foreach (var item in productItems.EnumerateArray())
                    {
                        var offer = FirstOffer(item);
                        var price = DecimalValue(offer, "Price");
                        if (price <= 0)
                        {
                            continue;
                        }

                        var nameComplete = Text(item, "nameComplete")
                            ?? Text(item, "name")
                            ?? productName;
                        items.Add(new
                        {
                            sku = ReferenceId(item) ?? Text(item, "itemId"),
                            name = nameComplete,
                            name_complete = nameComplete,
                            product_name = productName,
                            description,
                            brand,
                            category,
                            barcode = Text(item, "ean"),
                            image_url = FirstImage(item),
                            price,
                            stock = IntValue(offer, "AvailableQuantity"),
                            currency = _client.Currency
                        });

                        if (items.Count >= take)
                        {
                            break;
                        }
                    }
                }
            }

            return Ok(new { query = q, items });
        }
        catch (VtexIntegrationException ex)
        {
            return UpstreamError(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error buscando artículos VTEX {Query}", q);
            return StatusCode(502, new { error = "VTEX search failed" });
        }
    }

    [HttpGet("items/{sku}")]
    public async Task<IActionResult> GetItem(
        string sku,
        [FromQuery(Name = "stores")] string? storesFilter,
        CancellationToken ct)
    {
        try
        {
            var skuId = await _client.ResolveSkuIdAsync(sku, ct);
            if (string.IsNullOrWhiteSpace(skuId))
            {
                return NotFound(new { error = $"SKU {sku} no encontrado en VTEX" });
            }

            var context = await _client.GetSkuContextAsync(skuId, ct);
            var name = Text(context, "NameComplete")
                ?? Text(context, "ProductName")
                ?? Text(context, "SkuName")
                ?? sku;
            var productName = Text(context, "ProductName");
            var description = Text(context, "ProductDescription");
            var brand = Text(context, "BrandName");
            var category = ReadCategory(context);
            var images = ContextImages(context);
            var barcode = AlternateId(context, "Ean");
            var productId = Text(context, "ProductId");

            var price = await ReadPriceAsync(skuId, ct);
            var inventory = await _client.GetInventoryAsync(skuId, ct);
            var (stock, stockByStore) = ReadInventory(inventory, storesFilter);

            return Ok(new
            {
                sku,
                name,
                name_complete = name,
                product_name = productName,
                description,
                brand,
                category,
                barcode,
                image_url = images.FirstOrDefault(),
                images,
                product_id = productId,
                sku_id = skuId,
                price = price.Price,
                list_price = price.ListPrice,
                stock,
                currency = _client.Currency,
                stock_by_store = stockByStore
            });
        }
        catch (VtexIntegrationException ex)
        {
            return UpstreamError(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo artículo VTEX {Sku}", sku);
            return StatusCode(502, new { error = "VTEX item read failed" });
        }
    }

    [HttpGet("items/{sku}/pricing")]
    public async Task<IActionResult> GetPricing(string sku, CancellationToken ct)
    {
        try
        {
            var skuId = await _client.ResolveSkuIdAsync(sku, ct);
            if (string.IsNullOrWhiteSpace(skuId))
            {
                return NotFound(new { error = $"SKU {sku} no encontrado en VTEX" });
            }

            var fixedPrices = await _client.GetFixedPricesAsync(skuId, ct);
            var periods = BuildPeriods(fixedPrices);
            return Ok(new { sku, periods });
        }
        catch (VtexIntegrationException ex)
        {
            return UpstreamError(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo precios VTEX {Sku}", sku);
            return StatusCode(502, new { error = "VTEX pricing read failed" });
        }
    }

    [HttpGet("customers/scroll")]
    public async Task<IActionResult> ScrollCustomers(
        [FromQuery] string? token,
        [FromQuery] int size,
        CancellationToken ct)
    {
        var take = size <= 0 ? 100 : Math.Clamp(size, 1, 1000);

        try
        {
            var (data, nextToken, total) =
                await _client.ScrollCustomersAsync(token, take, ct);
            var customers = new List<object>();
            if (data.ValueKind != JsonValueKind.Array)
            {
                return Ok(new { customers, token = nextToken, total });
            }

            foreach (var customer in data.EnumerateArray())
            {
                var firstName = Text(customer, "firstName");
                var lastName = Text(customer, "lastName");
                customers.Add(new
                {
                    id = Text(customer, "id"),
                    email = Text(customer, "email"),
                    document = Text(customer, "document"),
                    first_name = firstName,
                    last_name = lastName,
                    name = string.Join(
                        " ",
                        new[] { firstName, lastName }
                            .Where(value => !string.IsNullOrWhiteSpace(value))),
                    phone = Text(customer, "phone"),
                    is_corporate = Text(customer, "isCorporate"),
                    corporate_name = Text(customer, "corporateName"),
                    trade_name = Text(customer, "tradeName"),
                    state_inscription = Text(customer, "stateInscription")
                });
            }

            return Ok(new { customers, token = nextToken, total });
        }
        catch (VtexIntegrationException ex)
        {
            return UpstreamError(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recorriendo clientes VTEX");
            return StatusCode(502, new { error = "VTEX customer scroll failed" });
        }
    }

    [HttpGet("customers/{documentOrEmail}")]
    public async Task<IActionResult> GetCustomer(
        string documentOrEmail, CancellationToken ct)
    {
        try
        {
            var customer = await _client.GetCustomerAsync(documentOrEmail, ct);
            if (customer is null)
            {
                return NotFound(new { error = "Cliente no encontrado en VTEX" });
            }

            return Ok(customer.Value);
        }
        catch (VtexIntegrationException ex)
        {
            return UpstreamError(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo cliente VTEX {Term}", documentOrEmail);
            return StatusCode(502, new { error = "VTEX customer read failed" });
        }
    }

    [HttpGet("customers/{documentOrEmail}/purchases")]
    public async Task<IActionResult> GetPurchases(
        string documentOrEmail,
        [FromQuery] int top,
        [FromQuery] int skip,
        CancellationToken ct)
    {
        var take = top <= 0 ? 10 : Math.Clamp(top, 1, 100);
        var offset = Math.Max(skip, 0);
        var page = offset / take + 1;

        try
        {
            var data = await _client.GetOrdersAsync(documentOrEmail, page, take, ct);
            var list = data.ValueKind == JsonValueKind.Object &&
                       data.TryGetProperty("list", out var orders)
                ? orders
                : default;
            var paging = data.ValueKind == JsonValueKind.Object &&
                         data.TryGetProperty("paging", out var pageInfo)
                ? pageInfo
                : default;

            return Ok(new
            {
                value = list.ValueKind == JsonValueKind.Array
                    ? (object)list
                    : Array.Empty<object>(),
                paging = paging.ValueKind == JsonValueKind.Object
                    ? (object)paging
                    : new { }
            });
        }
        catch (VtexIntegrationException ex)
        {
            return UpstreamError(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo compras VTEX {Term}", documentOrEmail);
            return StatusCode(502, new { error = "VTEX purchases read failed" });
        }
    }

    [HttpGet("orders")]
    public async Task<IActionResult> ListOrders(
        [FromQuery] int page,
        [FromQuery] int perPage,
        [FromQuery] string? dateFrom,
        [FromQuery] string? dateTo,
        CancellationToken ct)
    {
        try
        {
            var data = await _client.GetOrdersPageAsync(
                page <= 0 ? 1 : page,
                perPage <= 0 ? 100 : perPage,
                dateFrom,
                dateTo,
                ct);
            return Ok(data);
        }
        catch (VtexIntegrationException ex)
        {
            return UpstreamError(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listando pedidos VTEX");
            return StatusCode(502, new { error = "VTEX orders read failed" });
        }
    }

    [HttpGet("orders/{orderId}")]
    public async Task<IActionResult> GetOrder(string orderId, CancellationToken ct)
    {
        try
        {
            var order = await _client.GetOrderAsync(orderId, ct);
            return Ok(order);
        }
        catch (VtexIntegrationException ex)
        {
            return UpstreamError(ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error leyendo pedido VTEX {OrderId}", orderId);
            return StatusCode(502, new { error = "VTEX order read failed" });
        }
    }

    /// <summary>Paridad con /api/sap/catalog-reference; VTEX no lo necesita.</summary>
    [HttpGet("catalog-reference")]
    public IActionResult CatalogReference() =>
        Ok(new { groups = Array.Empty<object>(), manufacturers = Array.Empty<object>() });

    private async Task<(decimal Price, decimal ListPrice)> ReadPriceAsync(
        string skuId, CancellationToken ct)
    {
        try
        {
            var computed = await _client.GetComputedPriceAsync(skuId, 1, ct);
            var selling = DecimalValue(computed, "sellingPrice");
            if (selling > 0)
            {
                var list = DecimalValue(computed, "listPrice");
                return (selling, list > 0 ? list : selling);
            }
        }
        catch (VtexIntegrationException ex) when (ex.StatusCode is 400 or 404)
        {
            // Fall back to base/fixed prices.
        }

        try
        {
            var price = await _client.GetPriceAsync(skuId, ct);
            var basePrice = DecimalValue(price, "basePrice");
            if (price.TryGetProperty("fixedPrices", out var fixedPrices) &&
                fixedPrices.ValueKind == JsonValueKind.Array)
            {
                var now = DateTimeOffset.UtcNow;
                foreach (var row in fixedPrices.EnumerateArray())
                {
                    if (!string.Equals(
                            Text(row, "tradePolicyId"),
                            _client.SalesChannel.ToString(CultureInfo.InvariantCulture),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (IntValue(row, "minQuantity") > 1)
                    {
                        continue;
                    }

                    var (from, to) = DateRange(row);
                    if (from.HasValue && from.Value > now)
                    {
                        continue;
                    }
                    if (to.HasValue && to.Value < now)
                    {
                        continue;
                    }

                    var value = DecimalValue(row, "value");
                    if (value > 0)
                    {
                        var list = DecimalValue(row, "listPrice");
                        return (value, list > 0 ? list : value);
                    }
                }
            }

            if (basePrice > 0)
            {
                return (basePrice, basePrice);
            }
        }
        catch (VtexIntegrationException ex) when (ex.StatusCode is 400 or 404)
        {
            // No price configured for this SKU.
        }

        return (0m, 0m);
    }

    private (int Stock, Dictionary<string, int> ByStore) ReadInventory(
        JsonElement inventory, string? storesFilter)
    {
        var byStore = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (inventory.ValueKind != JsonValueKind.Object ||
            !inventory.TryGetProperty("balance", out var balance) ||
            balance.ValueKind != JsonValueKind.Array)
        {
            return (0, byStore);
        }

        var reverse = _client.WarehouseCodeByVtexId();
        var filter = (storesFilter ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in balance.EnumerateArray())
        {
            var warehouseId = Text(row, "warehouseId");
            if (string.IsNullOrWhiteSpace(warehouseId))
            {
                continue;
            }

            var code = reverse.TryGetValue(warehouseId, out var mapped)
                ? mapped
                : warehouseId;
            if (filter.Count > 0 && !filter.Contains(code))
            {
                continue;
            }

            var available = IntValue(row, "totalQuantity") -
                            IntValue(row, "reservedQuantity");
            if (available < 0)
            {
                available = 0;
            }

            // VTEX uses hasUnlimitedQuantity for items that are always
            // sellable; the public offer reports 99999 in that case.
            if (row.TryGetProperty("hasUnlimitedQuantity", out var unlimited) &&
                unlimited.ValueKind == JsonValueKind.True)
            {
                available = 99999;
            }

            byStore[code] = byStore.GetValueOrDefault(code) + available;
        }

        return (byStore.Values.Sum(), byStore);
    }

    private static List<object> BuildPeriods(JsonElement fixedPrices)
    {
        var result = new List<object>();
        if (fixedPrices.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        var groups = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        foreach (var row in fixedPrices.EnumerateArray())
        {
            var (from, to) = DateRange(row);
            var key = $"{from:O}|{to:O}";
            if (!groups.TryGetValue(key, out var rows))
            {
                rows = new List<JsonElement>();
                groups[key] = rows;
            }
            rows.Add(row.Clone());
        }

        foreach (var rows in groups.Values)
        {
            var baseRow = rows
                .Where(row => IntValue(row, "minQuantity") <= 1)
                .OrderBy(row => IntValue(row, "minQuantity"))
                .FirstOrDefault();
            if (baseRow.ValueKind == JsonValueKind.Undefined)
            {
                baseRow = rows[0];
            }

            var price = DecimalValue(baseRow, "value");
            var listPrice = DecimalValue(baseRow, "listPrice");
            var discount = listPrice > 0 && price > 0
                ? Math.Round((listPrice - price) / listPrice * 100m, 2)
                : 0m;
            var (from, to) = DateRange(baseRow);
            var tiers = rows
                .Where(row => IntValue(row, "minQuantity") > 1)
                .Select(row =>
                {
                    var tierPrice = DecimalValue(row, "value");
                    var tierList = DecimalValue(row, "listPrice");
                    var tierDiscount = tierList > 0 && tierPrice > 0
                        ? Math.Round((tierList - tierPrice) / tierList * 100m, 2)
                        : 0m;
                    return new
                    {
                        quantity = IntValue(row, "minQuantity"),
                        price = tierPrice,
                        discount_percent = tierDiscount
                    };
                })
                .OrderBy(tier => tier.quantity)
                .ToList();

            result.Add(new
            {
                price_list = 1,
                valid_from = from?.ToString("O"),
                valid_to = to?.ToString("O"),
                price,
                discount_percent = discount,
                tiers
            });
        }

        return result;
    }

    private IActionResult UpstreamError(VtexIntegrationException ex) =>
        ex.StatusCode switch
        {
            404 => NotFound(new { error = "No encontrado en VTEX" }),
            401 or 403 => StatusCode(502, new
            {
                error = "VTEX rechazó las credenciales o permisos del PaaS"
            }),
            _ => StatusCode(502, new { error = "VTEX upstream error" })
        };

    private static string? CleanCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parts = value.Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? null : parts[^1];
    }

    private static string? ReadCategory(JsonElement context)
    {
        if (context.ValueKind == JsonValueKind.Object &&
            context.TryGetProperty("ProductCategories", out var categories) &&
            categories.ValueKind == JsonValueKind.Object)
        {
            string? last = null;
            foreach (var property in categories.EnumerateObject())
            {
                var value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : null;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    last = value;
                }
            }

            return CleanCategory(last);
        }

        var list = StringArray(context, "Categories");
        return CleanCategory(list.LastOrDefault());
    }

    private static List<string> ContextImages(JsonElement context)
    {
        var result = new List<string>();
        if (context.ValueKind != JsonValueKind.Object ||
            !context.TryGetProperty("Images", out var images) ||
            images.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var image in images.EnumerateArray())
        {
            var url = Text(image, "ImageUrl");
            if (!string.IsNullOrWhiteSpace(url))
            {
                result.Add(url);
            }
        }

        return result;
    }

    private static List<string> ImageUrls(JsonElement item)
    {
        var result = new List<string>();
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("images", out var images) ||
            images.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var image in images.EnumerateArray())
        {
            var url = Text(image, "imageUrl");
            if (!string.IsNullOrWhiteSpace(url))
            {
                result.Add(url);
            }
        }

        return result;
    }

    private static string? AlternateId(JsonElement context, string key)
    {
        if (context.ValueKind == JsonValueKind.Object &&
            context.TryGetProperty("AlternateIds", out var alternateIds) &&
            alternateIds.ValueKind == JsonValueKind.Object &&
            alternateIds.TryGetProperty(key, out var value))
        {
            return value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : value.ToString();
        }

        return null;
    }

    private static JsonElement FirstOffer(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("sellers", out var sellers) &&
            sellers.ValueKind == JsonValueKind.Array)
        {
            foreach (var seller in sellers.EnumerateArray())
            {
                if (seller.TryGetProperty("commertialOffer", out var offer) &&
                    offer.ValueKind == JsonValueKind.Object)
                {
                    return offer;
                }
            }
        }

        return default;
    }

    private static string? FirstImage(JsonElement item)
    {
        if (item.ValueKind == JsonValueKind.Object &&
            item.TryGetProperty("images", out var images) &&
            images.ValueKind == JsonValueKind.Array)
        {
            foreach (var image in images.EnumerateArray())
            {
                var url = Text(image, "imageUrl");
                if (!string.IsNullOrWhiteSpace(url))
                {
                    return url;
                }
            }
        }

        return null;
    }

    private static string? ReferenceId(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object ||
            !item.TryGetProperty("referenceId", out var references) ||
            references.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var reference in references.EnumerateArray())
        {
            if (string.Equals(
                    Text(reference, "Key"), "RefId", StringComparison.OrdinalIgnoreCase))
            {
                return Text(reference, "Value");
            }
        }

        return null;
    }

    private static List<string> StringArray(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return array.EnumerateArray()
            .Select(value => value.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToList();
    }

    private static string? Text(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    private static decimal DecimalValue(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(name, out var value))
        {
            return 0m;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDecimal(out var number) => number,
            JsonValueKind.String when decimal.TryParse(
                value.GetString(),
                NumberStyles.Any,
                CultureInfo.InvariantCulture,
                out var parsed) => parsed,
            _ => 0m
        };
    }

    private static int IntValue(JsonElement element, string name)
    {
        var value = DecimalValue(element, name);
        return value == 0m ? 0 : (int)value;
    }

    private static (DateTimeOffset? From, DateTimeOffset? To) DateRange(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object ||
            !row.TryGetProperty("dateRange", out var range) ||
            range.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        return (ParseDate(Text(range, "from")), ParseDate(Text(range, "to")));
    }

    private static DateTimeOffset? ParseDate(string? value) =>
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
}
