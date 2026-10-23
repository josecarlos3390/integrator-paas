using System.Net;
using System.Text.Json;
using Integration.Shared.Configuration;
using Integration.Shared.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integration.Shared.Clients;

/// <summary>
/// Read-only VTEX client used by the PaasVTEX facade.
///
/// VTEX has two different hosts:
/// * catalog / inventory / OMS / Master Data: https://{account}.vtexcommercestable.com.br
/// * pricing: https://api.vtex.com/{account}
///
/// No write methods are exposed on purpose: the bot is informational.
/// </summary>
public class VtexClient
{
    private readonly HttpClient _http;
    private readonly VtexConfig _config;
    private readonly ILogger<VtexClient> _logger;

    public VtexClient(
        HttpClient http,
        IOptions<VtexConfig> config,
        ILogger<VtexClient> logger)
    {
        _http = http;
        _config = config.Value;
        _logger = logger;
    }

    public bool IsConfigured => _config.IsConfigured;

    private async Task<JsonElement> GetJsonAsync(
        string url,
        CancellationToken ct,
        Action<HttpRequestMessage>? configure = null)
    {
        if (!_config.IsConfigured)
        {
            throw new VtexIntegrationException(503, "VTEX no está configurado en el PaaS.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("X-VTEX-API-AppKey", _config.AppKey);
        request.Headers.TryAddWithoutValidation("X-VTEX-API-AppToken", _config.AppToken);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        configure?.Invoke(request);

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "VTEX {Status} en {Url}: {Body}",
                (int)response.StatusCode, url, body.Length > 500 ? body[..500] : body);
            throw new VtexIntegrationException((int)response.StatusCode, body);
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return default;
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Resolves a bot SKU (RefId, alternate id, EAN or numeric SKU id) to the
    /// numeric VTEX SKU id.
    /// </summary>
    public async Task<string?> ResolveSkuIdAsync(string sku, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return null;
        }

        var term = sku.Trim();
        if (long.TryParse(term, out _))
        {
            return term;
        }

        var paths = new[]
        {
            $"api/catalog_system/pvt/sku/stockkeepingunitidbyrefid/{Uri.EscapeDataString(term)}",
            $"api/catalog_system/pvt/sku/stockkeepingunitbyalternateId/{Uri.EscapeDataString(term)}",
            $"api/catalog_system/pvt/sku/stockkeepingunitbyean/{Uri.EscapeDataString(term)}"
        };

        foreach (var path in paths)
        {
            try
            {
                var result = await GetJsonAsync($"{_config.BaseUrl}/{path}", ct);
                var id = result.ValueKind switch
                {
                    JsonValueKind.Number => result.GetInt64().ToString(),
                    JsonValueKind.String => result.GetString(),
                    JsonValueKind.Object when result.TryGetProperty("Id", out var idProperty)
                        => idProperty.ValueKind == JsonValueKind.Number
                            ? idProperty.GetInt64().ToString()
                            : idProperty.GetString(),
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return id;
                }
            }
            catch (VtexIntegrationException ex) when (ex.StatusCode == 404)
            {
                // Try the next identifier.
            }
        }

        return null;
    }

    public Task<JsonElement> GetSkuContextAsync(string skuId, CancellationToken ct) =>
        GetJsonAsync(
            $"{_config.BaseUrl}/api/catalog_system/pvt/sku/stockkeepingunitbyid/{Uri.EscapeDataString(skuId)}",
            ct);

    public Task<JsonElement> GetComputedPriceAsync(
        string skuId, int quantity, CancellationToken ct) =>
        GetJsonAsync(
            $"{_config.PricingUrl}/pricing/prices/{Uri.EscapeDataString(skuId)}/computed/{_config.SalesChannel}?quantity={Math.Max(quantity, 1)}",
            ct);

    public Task<JsonElement> GetPriceAsync(string skuId, CancellationToken ct) =>
        GetJsonAsync(
            $"{_config.PricingUrl}/pricing/prices/{Uri.EscapeDataString(skuId)}",
            ct);

    public Task<JsonElement> GetFixedPricesAsync(string skuId, CancellationToken ct) =>
        GetJsonAsync(
            $"{_config.PricingUrl}/pricing/prices/{Uri.EscapeDataString(skuId)}/fixed/{_config.SalesChannel}",
            ct);

    public Task<JsonElement> GetInventoryAsync(string skuId, CancellationToken ct) =>
        GetJsonAsync(
            $"{_config.BaseUrl}/api/logistics/pvt/inventory/skus/{Uri.EscapeDataString(skuId)}",
            ct);

    public Task<JsonElement> GetWarehousesAsync(CancellationToken ct) =>
        GetJsonAsync(
            $"{_config.BaseUrl}/api/logistics/pvt/configuration/warehouses",
            ct);

    public Task<JsonElement> SearchProductsAsync(
        string? query,
        int from,
        int to,
        CancellationToken ct,
        string? filter = null,
        string? priceRange = null)
    {
        var safeFrom = Math.Max(from, 0);
        var safeTo = Math.Max(to, safeFrom);
        var text = string.IsNullOrWhiteSpace(query)
            ? string.Empty
            : $"ft={Uri.EscapeDataString(query.Trim())}&";
        var fq = string.IsNullOrWhiteSpace(filter)
            ? string.Empty
            : $"fq={Uri.EscapeDataString(filter)}&";
        if (!string.IsNullOrWhiteSpace(priceRange))
        {
            fq += $"fq=P:{Uri.EscapeDataString(priceRange)}&";
        }
        return GetJsonAsync(
            $"{_config.BaseUrl}/api/catalog_system/pub/products/search?{text}{fq}_from={safeFrom}&_to={safeTo}&sc={_config.SalesChannel}",
            ct);
    }

    public Task<JsonElement> GetExportPageAsync(
        int offset,
        int limit,
        string? brandId,
        string? priceRange,
        CancellationToken ct) =>
        SearchProductsAsync(
            null,
            offset,
            offset + Math.Clamp(limit, 1, 50) - 1,
            ct,
            string.IsNullOrWhiteSpace(brandId) ? null : $"B:{brandId}",
            priceRange);

    public Task<JsonElement> GetBrandsAsync(CancellationToken ct) =>
        GetJsonAsync(
            $"{_config.BaseUrl}/api/catalog_system/pvt/brand/list",
            ct);

    public async Task<JsonElement?> GetCustomerAsync(
        string documentOrEmail, CancellationToken ct)
    {
        var term = documentOrEmail.Trim();
        var where = term.Contains('@')
            ? $"email={term}"
            : $"document={term}";
        var url =
            $"{_config.BaseUrl}/api/dataentities/CL/search" +
            $"?_where={Uri.EscapeDataString(where)}" +
            "&_fields=id,email,document,firstName,lastName,phone" +
            "&_sort=firstName%20ASC";

        var data = await GetJsonAsync(url, ct);
        if (data.ValueKind != JsonValueKind.Array || data.GetArrayLength() == 0)
        {
            return null;
        }

        return data[0].Clone();
    }

    /// <summary>
    /// Recorre todos los clientes CL con el scroll de Master Data.
    /// El token viaja en el header X-VTEX-MD-TOKEN.
    /// </summary>
    public async Task<(JsonElement Data, string? Token, int? Total)>
        ScrollCustomersAsync(string? token, int size, CancellationToken ct)
    {
        if (!_config.IsConfigured)
        {
            throw new VtexIntegrationException(503, "VTEX no está configurado en el PaaS.");
        }

        var query = $"?_size={Math.Clamp(size, 1, 1000)}";
        if (!string.IsNullOrWhiteSpace(token))
        {
            query += $"&_token={Uri.EscapeDataString(token)}";
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{_config.BaseUrl}/api/dataentities/CL/scroll{query}");
        request.Headers.TryAddWithoutValidation("X-VTEX-API-AppKey", _config.AppKey);
        request.Headers.TryAddWithoutValidation("X-VTEX-API-AppToken", _config.AppToken);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");

        using var response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new VtexIntegrationException((int)response.StatusCode, body);
        }

        var next = response.Headers.TryGetValues("X-VTEX-MD-TOKEN", out var tokens)
            ? tokens.FirstOrDefault()
            : null;
        int? total = null;
        if (response.Headers.TryGetValues("REST-Content-Total", out var totals) &&
            int.TryParse(totals.FirstOrDefault(), out var parsedTotal))
        {
            total = parsedTotal;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return (default, next, total);
        }

        using var document = JsonDocument.Parse(body);
        return (document.RootElement.Clone(), next, total);
    }

    public Task<JsonElement> GetOrdersAsync(
        string q, int page, int perPage, CancellationToken ct) =>
        GetJsonAsync(
            $"{_config.BaseUrl}/api/oms/pvt/orders" +
            $"?q={Uri.EscapeDataString(q)}&page={Math.Max(page, 1)}&per_page={Math.Clamp(perPage, 1, 100)}",
            ct);

    public Task<JsonElement> GetOrdersPageAsync(
        int page,
        int perPage,
        string? createdFrom,
        string? createdTo,
        CancellationToken ct)
    {
        var url =
            $"{_config.BaseUrl}/api/oms/pvt/orders" +
            $"?page={Math.Max(page, 1)}&per_page={Math.Clamp(perPage, 1, 100)}";
        if (!string.IsNullOrWhiteSpace(createdFrom) &&
            !string.IsNullOrWhiteSpace(createdTo))
        {
            var filter = $"creationDate:[{createdFrom} TO {createdTo}]";
            url += $"&f_creationDate={Uri.EscapeDataString(filter)}";
        }

        return GetJsonAsync(url, ct);
    }

    public Task<JsonElement> GetOrderAsync(string orderId, CancellationToken ct) =>
        GetJsonAsync(
            $"{_config.BaseUrl}/api/oms/pvt/orders/{Uri.EscapeDataString(orderId)}",
            ct);

    /// <summary>
    /// Maps the configured store code -> warehouse id and returns the reverse
    /// lookup used to build stock_by_store.
    /// </summary>
    public IReadOnlyDictionary<string, string> WarehouseCodeByVtexId()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in _config.WarehouseMap)
        {
            if (!string.IsNullOrWhiteSpace(pair.Key) &&
                !string.IsNullOrWhiteSpace(pair.Value))
            {
                result[pair.Value] = pair.Key;
            }
        }

        return result;
    }

    public int SalesChannel => _config.SalesChannel;
    public string Currency => _config.Currency;
}
