using System.Net;
using FluentAssertions;
using Integration.Shared.Clients;

namespace Integration.Shared.Tests.Clients;

public class CookieContainerHandlerTests
{
    [Fact]
    public async Task SendAsync_InjectsCookiesFromContainer()
    {
        var container = new CookieContainer();
        container.Add(new Cookie("session", "abc123", "/", "example.com"));

        var innerHandler = new TestHandler(request =>
        {
            request.Headers.Should().ContainKey("Cookie");
            request.Headers.GetValues("Cookie").Should().Contain("session=abc123");
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new CookieContainerHandler(container) { InnerHandler = innerHandler };
        var client = new HttpClient(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_CapturesSetCookieHeaders()
    {
        var container = new CookieContainer();

        var innerHandler = new TestHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("Set-Cookie", "auth=token123; Path=/");
            return response;
        });

        var handler = new CookieContainerHandler(container) { InnerHandler = innerHandler };
        var client = new HttpClient(handler);

        var request = new HttpRequestMessage(HttpMethod.Get, "http://example.com/api");
        await client.SendAsync(request);

        var cookies = container.GetCookies(new Uri("http://example.com/api"));
        cookies.Should().Contain(c => c.Name == "auth" && c.Value == "token123");
    }

    [Fact]
    public async Task SendAsync_WhenNoCookies_DoesNotAddCookieHeader()
    {
        var container = new CookieContainer();

        string? cookieHeaderValue = null;
        var innerHandler = new TestHandler(request =>
        {
            if (request.Headers.TryGetValues("Cookie", out var values))
            {
                cookieHeaderValue = values.FirstOrDefault();
            }
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var handler = new CookieContainerHandler(container) { InnerHandler = innerHandler };
        var client = new HttpClient(handler);

        await client.GetAsync("http://example.com/api");

        cookieHeaderValue.Should().BeNull();
    }

    private class TestHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _callback;

        public TestHandler(Func<HttpRequestMessage, HttpResponseMessage> callback)
        {
            _callback = callback;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_callback(request));
        }
    }
}
