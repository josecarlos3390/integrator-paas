using System.Net;

namespace Integration.Shared.Clients;

/// <summary>
/// DelegatingHandler that manages cookies in memory for a single HttpClient instance.
/// Required because IHttpClientFactory does not provide per-instance CookieContainer
/// when handlers are pooled; SAP Service Layer needs isolated session cookies per tenant.
/// </summary>
public class CookieContainerHandler : DelegatingHandler
{
    private readonly CookieContainer _cookieContainer;

    public CookieContainerHandler(CookieContainer cookieContainer)
    {
        _cookieContainer = cookieContainer;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Request URI is required for cookie handling");

        // Inject existing cookies into the request
        request.Headers.Remove("Cookie");
        var cookieHeader = _cookieContainer.GetCookieHeader(uri);
        if (!string.IsNullOrEmpty(cookieHeader))
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookieHeader);
        }

        var response = await base.SendAsync(request, cancellationToken);

        // Capture Set-Cookie headers from the response
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var cookie in setCookies)
            {
                _cookieContainer.SetCookies(uri, cookie);
            }
        }

        return response;
    }
}
