using System.Data;
using System.Net.Http.Json;
using System.Text.Json;
using Integration.Shared.Configuration;
using Integration.Shared.Dtos;
using Integration.Shared.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integration.Shared.Clients;

/// <summary>
/// HTTP client for the SAP Business One Service Layer.
/// Automatically manages login, session (B1SESSION cookie)
/// and re-login upon 401 Unauthorized responses.
/// </summary>
public class ServiceLayerClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ServiceLayerClient> _logger;
    private readonly SapConfig _config;
    private readonly SemaphoreSlim _loginLock = new(1, 1);
    private string? _sessionId;
    private bool _isLoggedIn;

    public ServiceLayerClient(
        HttpClient httpClient,
        SapConfig config,
        ILogger<ServiceLayerClient> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Gets an A/R invoice by its DocEntry.
    /// </summary>
    public async Task<SapInvoice> GetInvoiceAsync(int docEntry, CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        var url = $"/b1s/v1/Invoices({docEntry})?$select=DocEntry,DocNum,CardCode,CardName,DocDate,DocTotal,DocCurrency,DocumentLines";
        var response = await _httpClient.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _isLoggedIn = false;
            await EnsureLoggedInAsync(ct);
            response = await _httpClient.GetAsync(url, ct);
        }

        response.EnsureSuccessStatusCode();
        var invoice = await response.Content.ReadFromJsonAsync<SapInvoice>(ct);
        return invoice ?? throw new SapIntegrationException($"Invoice {docEntry} not found or empty response.");
    }

    /// <summary>
    /// Gets a Business Partner (customer/vendor) by its CardCode.
    /// </summary>
    public async Task<SapBusinessPartner> GetBusinessPartnerAsync(string cardCode, CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        var url = $"/b1s/v1/BusinessPartners('{Uri.EscapeDataString(cardCode)}')";
        var response = await _httpClient.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _isLoggedIn = false;
            await EnsureLoggedInAsync(ct);
            response = await _httpClient.GetAsync(url, ct);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new SapIntegrationException($"Business Partner '{cardCode}' not found in SAP.", isBusinessError: true);
        }

        response.EnsureSuccessStatusCode();
        var bp = await response.Content.ReadFromJsonAsync<SapBusinessPartner>(ct);
        return bp ?? throw new SapIntegrationException($"Business Partner '{cardCode}' returned empty response.");
    }

    /// <summary>
    /// Gets only the bank-related fields of a vendor (VENDOR_BANK_ALERT flow).
    /// </summary>
    public async Task<SapBusinessPartner> GetVendorBankInfoAsync(string cardCode, CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        var url = $"/b1s/v1/BusinessPartners('{Uri.EscapeDataString(cardCode)}')?$select=CardCode,CardName,CardType,DefaultBankCode,DefaultBranch,DefaultAccount,IBAN";
        var response = await _httpClient.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _isLoggedIn = false;
            await EnsureLoggedInAsync(ct);
            response = await _httpClient.GetAsync(url, ct);
        }

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new SapIntegrationException($"Business Partner '{cardCode}' not found in SAP.", isBusinessError: true);
        }

        response.EnsureSuccessStatusCode();
        var bp = await response.Content.ReadFromJsonAsync<SapBusinessPartner>(ct);
        return bp ?? throw new SapIntegrationException($"Business Partner '{cardCode}' returned empty response.");
    }

    /// <summary>
    /// Gets a page of suppliers with their bank fields (baseline backfill).
    /// Service Layer caps pages at 20 records server-side and ignores $top,
    /// so pagination must follow odata.nextLink: HasMore = nextLink present.
    /// </summary>
    public async Task<(List<SapBusinessPartner> Items, bool HasMore)> GetVendorBankInfoPageAsync(int skip, CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        var url = $"/b1s/v1/BusinessPartners?$filter=CardType eq 'cSupplier'&$select=CardCode,CardName,CardType,DefaultBankCode,DefaultBranch,DefaultAccount,IBAN&$skip={skip}";
        var response = await _httpClient.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _isLoggedIn = false;
            await EnsureLoggedInAsync(ct);
            response = await _httpClient.GetAsync(url, ct);
        }

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var items = new List<SapBusinessPartner>();

        foreach (var element in result.GetProperty("value").EnumerateArray())
        {
            var bp = element.Deserialize<SapBusinessPartner>();
            if (bp is not null) items.Add(bp);
        }

        var hasMore = result.TryGetProperty("odata.nextLink", out _);
        return (items, hasMore);
    }

    /// <summary>
    /// Resolves a SAP internal user key (OCRD.UserSign2) to a display name
    /// via the Users entity. Returns null when the user cannot be resolved.
    /// </summary>
    public async Task<string?> GetUserNameAsync(int internalKey, CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        var url = $"/b1s/v1/Users?$filter=InternalKey eq {internalKey}&$select=UserCode,UserName";
        var response = await _httpClient.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _isLoggedIn = false;
            await EnsureLoggedInAsync(ct);
            response = await _httpClient.GetAsync(url, ct);
        }

        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var value = result.GetProperty("value");
        if (value.GetArrayLength() == 0) return null;

        var user = value[0];
        var name = user.TryGetProperty("UserName", out var n) ? n.GetString() : null;
        var code = user.TryGetProperty("UserCode", out var c) ? c.GetString() : null;
        return string.IsNullOrWhiteSpace(name) ? code : name;
    }

    /// <summary>
    /// Creates a sales order in SAP B1.
    /// </summary>
    public async Task<(int DocEntry, int DocNum)> CreateOrderAsync(SapOrderPayload payload, CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        var response = await _httpClient.PostAsJsonAsync("/b1s/v1/Orders", payload, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _isLoggedIn = false;
            await EnsureLoggedInAsync(ct);
            response = await _httpClient.PostAsJsonAsync("/b1s/v1/Orders", payload, ct);
        }

        if (!response.IsSuccessStatusCode)
        {
            var errorJson = await response.Content.ReadAsStringAsync(ct);
            var sapError = TryParseSapError(errorJson);
            throw new SapIntegrationException(
                sapError?.Message ?? $"SAP order creation failed: {response.StatusCode}",
                sapError?.Code,
                isBusinessError: (int)response.StatusCode >= 400 && (int)response.StatusCode < 500);
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var docEntry = result.GetProperty("DocEntry").GetInt32();
        var docNum = result.TryGetProperty("DocNum", out var dn) ? dn.GetInt32() : 0;
        return (docEntry, docNum);
    }

    /// <summary>
    /// Checks if an order with the given NumAtCard already exists (CRM→SAP idempotency).
    /// </summary>
    public async Task<int?> GetOrderByNumAtCardAsync(string numAtCard, CancellationToken ct = default)
    {
        await EnsureLoggedInAsync(ct);

        var filter = Uri.EscapeDataString($"NumAtCard eq '{numAtCard}'");
        var url = $"/b1s/v1/Orders?$filter={filter}&$select=DocEntry,DocNum";
        var response = await _httpClient.GetAsync(url, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            _isLoggedIn = false;
            await EnsureLoggedInAsync(ct);
            response = await _httpClient.GetAsync(url, ct);
        }

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var value = result.GetProperty("value");
        if (value.GetArrayLength() == 0) return null;

        return value[0].GetProperty("DocEntry").GetInt32();
    }

    private async Task EnsureLoggedInAsync(CancellationToken ct)
    {
        if (_isLoggedIn) return;

        await _loginLock.WaitAsync(ct);
        try
        {
            if (_isLoggedIn) return;

            var loginPayload = new
            {
                CompanyDB = _config.CompanyDB,
                UserName = _config.UserName,
                Password = _config.Password
            };

            _logger.LogInformation("SAP Login Request: POST {Url} with CompanyDB={CompanyDB}, UserName={UserName}",
                _httpClient.BaseAddress + "/b1s/v1/Login", _config.CompanyDB, _config.UserName);

            var options = new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = null // Usar PascalCase exacto de las propiedades
            };
            var response = await _httpClient.PostAsJsonAsync("/b1s/v1/Login", loginPayload, options, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("SAP Login failed with {StatusCode}. Response: {Body}", (int)response.StatusCode, errorBody);
                throw new HttpRequestException($"SAP Login failed: {(int)response.StatusCode} - {errorBody}");
            }

            var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            _sessionId = result.GetProperty("SessionId").GetString();

            _httpClient.DefaultRequestHeaders.Remove("Cookie");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", $"B1SESSION={_sessionId}");

            _isLoggedIn = true;
            _logger.LogInformation("SAP Service Layer login successful. SessionId={SessionId}", _sessionId);
        }
        finally
        {
            _loginLock.Release();
        }
    }

    private static SapError? TryParseSapError(string json)
    {
        try
        {
            var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var c) ? c.GetInt32() : (int?)null;
                var message = error.TryGetProperty("message", out var m) && m.TryGetProperty("value", out var v)
                    ? v.GetString() : null;
                return new SapError(code, message ?? "Unknown SAP error");
            }
        }
        catch { /* best effort */ }
        return null;
    }

    private record SapError(int? Code, string Message);
}
