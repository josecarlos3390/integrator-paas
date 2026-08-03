using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Integration.Shared.Dtos;
using Microsoft.Extensions.Logging;

namespace Integration.Worker.Services;

/// <summary>
/// Sends processing results back to the caller via HTTP callback.
/// Fire-and-forget: failures are logged but do not block the worker.
/// Includes SSRF protection to prevent callbacks to internal endpoints.
/// </summary>
public class CallbackNotifier
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CallbackNotifier> _logger;

    public CallbackNotifier(
        IHttpClientFactory httpClientFactory,
        ILogger<CallbackNotifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task NotifyAsync(
        string callbackUrl,
        IngestionResult result,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(callbackUrl))
            return;

        if (!IsValidCallbackUrl(callbackUrl))
        {
            _logger.LogWarning("Callback URL rejected due to SSRF policy: {Url}", callbackUrl);
            return;
        }

        try
        {
            using var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            var response = await client.PostAsJsonAsync(callbackUrl, result, ct);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation(
                    "Callback notified successfully: {Url} responded {StatusCode} for request {RequestId}",
                    callbackUrl, (int)response.StatusCode, result.RequestId);
            }
            else
            {
                _logger.LogWarning(
                    "Callback returned non-success: {Url} responded {StatusCode} for request {RequestId}",
                    callbackUrl, (int)response.StatusCode, result.RequestId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to send callback to {Url} for request {RequestId}",
                callbackUrl, result.RequestId);
        }
    }

    /// <summary>
    /// Validates that the callback URL points to a public endpoint and not to
    /// internal/private networks (SSRF protection).
    /// </summary>
    private static bool IsValidCallbackUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return false;

        if (string.IsNullOrWhiteSpace(uri.Host))
            return false;

        // Reject localhost and loopback
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return false;

        if (uri.HostNameType == UriHostNameType.IPv4)
        {
            if (!IPAddress.TryParse(uri.Host, out var ip))
                return false;

            if (IPAddress.IsLoopback(ip))
                return false;

            if (IsPrivateIp(ip))
                return false;
        }

        return true;
    }

    private static bool IsPrivateIp(IPAddress ip)
    {
        var bytes = ip.GetAddressBytes();
        if (bytes.Length != 4)
            return false;

        // 10.0.0.0/8
        if (bytes[0] == 10)
            return true;

        // 172.16.0.0/12
        if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
            return true;

        // 192.168.0.0/16
        if (bytes[0] == 192 && bytes[1] == 168)
            return true;

        // 169.254.0.0/16 (link-local)
        if (bytes[0] == 169 && bytes[1] == 254)
            return true;

        return false;
    }
}
