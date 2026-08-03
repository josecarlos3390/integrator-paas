using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Integration.Shared.Domain;
using Integration.Shared.Dtos;
using Microsoft.Extensions.Logging;

namespace Integration.Shared.Connectors.HansaCrm;

/// <summary>
/// Typed HTTP client for HansaCRM REST APIs.
/// All payloads are sent to a single integration endpoint.
/// Automatically injects the Bearer token on every request.
/// </summary>
public class HansaCrmClient
{
    private readonly HttpClient _httpClient;
    private readonly HansaCrmAuthService _authService;
    private readonly HansaCrmConfig _config;
    private readonly string _tenantId;
    private readonly ILogger<HansaCrmClient> _logger;

    public HansaCrmClient(
        HttpClient httpClient,
        HansaCrmAuthService authService,
        HansaCrmConfig config,
        string tenantId,
        ILogger<HansaCrmClient> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _config = config;
        _tenantId = tenantId;
        _logger = logger;
    }

    /// <summary>
    /// Sends any payload to the single HansaCRM integration endpoint.
    /// The caller is responsible for building the correct wrapper (object + entry).
    /// </summary>
    public async Task<CrmApiResponse<TResponse>> SendAsync<TPayload, TResponse>(TPayload payload, CancellationToken ct = default)
    {
        var token = await _authService.GetAccessTokenAsync(_tenantId, ct);

        var request = new HttpRequestMessage(HttpMethod.Post, _config.IntegrationEndpoint)
        {
            Content = JsonContent.Create(payload, options: new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase // HansaCRM expects camelCase; snake_case fields use JsonPropertyName
            })
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        _logger.LogDebug("HansaCRM POST {Endpoint} for tenant {TenantId}", _config.IntegrationEndpoint, _tenantId);

        var response = await _httpClient.SendAsync(request, ct);

        var apiResponse = new CrmApiResponse<TResponse>
        {
            StatusCode = response.StatusCode
        };

        if (response.IsSuccessStatusCode)
        {
            try
            {
                apiResponse.Content = await response.Content.ReadFromJsonAsync<TResponse>(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "HansaCRM response body could not be deserialized");
            }
        }
        else
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            apiResponse.ErrorMessage = errorBody;
            _logger.LogWarning("HansaCRM returned {StatusCode}: {Body}", (int)response.StatusCode, errorBody);
        }

        return apiResponse;
    }
}
