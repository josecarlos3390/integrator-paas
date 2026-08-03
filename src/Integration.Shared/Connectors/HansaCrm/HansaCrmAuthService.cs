using System.Net.Http.Headers;
using System.Net.Http.Json;
using Integration.Shared.Observability;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integration.Shared.Connectors.HansaCrm;

/// <summary>
/// Handles OAuth2 token acquisition and caching for HansaCRM.
/// Tokens are cached per tenant to avoid unnecessary re-authentication.
/// </summary>
public class HansaCrmAuthService
{
    private readonly HttpClient _httpClient;
    private readonly IOptions<HansaCrmConfig> _config;
    private readonly IMemoryCache _cache;
    private readonly ILogger<HansaCrmAuthService> _logger;

    public HansaCrmAuthService(
        HttpClient httpClient,
        IOptions<HansaCrmConfig> config,
        IMemoryCache cache,
        ILogger<HansaCrmAuthService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(string tenantId, CancellationToken ct = default)
    {
        var cacheKey = $"hansa_token:{tenantId}";
        if (_cache.TryGetValue(cacheKey, out string? token) && !string.IsNullOrEmpty(token))
        {
            _logger.LogDebug("HansaCRM token cache hit for tenant {TenantId}", tenantId);
            return token;
        }

        _logger.LogInformation("Authenticating with HansaCRM for tenant {TenantId}", tenantId);
        IntegrationMetrics.RecordTokenRefresh(tenantId, "cache_miss");
        var newToken = await AuthenticateAsync(ct);

        var ttl = TimeSpan.FromSeconds(_config.Value.TokenCacheSeconds);
        _cache.Set(cacheKey, newToken, ttl);

        return newToken;
    }

    private async Task<string> AuthenticateAsync(CancellationToken ct)
    {
        var cfg = _config.Value;

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = cfg.ClientId,
            ["client_secret"] = cfg.ClientSecret,
            ["grant_type"] = "password",
            ["authenticated_userid"] = cfg.AuthenticatedUserId,
            ["scope"] = cfg.Scope,
            ["provision_key"] = cfg.ProvisionKey,
        });

        content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");

        var tokenUrl = $"{cfg.BaseUrl}oauth2/token";
        var response = await _httpClient.PostAsync(tokenUrl, content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("HansaCRM authentication failed: {StatusCode} {Body}", (int)response.StatusCode, errorBody);
            response.EnsureSuccessStatusCode();
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<HansaCrmTokenResponse>(ct);
        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("HansaCRM returned an empty token response.");
        }

        _logger.LogInformation("HansaCRM token acquired. Expires in {ExpiresIn}s", tokenResponse.ExpiresIn);
        return tokenResponse.AccessToken;
    }
}
