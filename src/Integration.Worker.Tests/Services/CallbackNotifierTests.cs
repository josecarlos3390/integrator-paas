using FluentAssertions;
using Integration.Shared.Dtos;
using Integration.Worker.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Net;
using Xunit;

namespace Integration.Worker.Tests.Services;

public class CallbackNotifierTests
{
    [Fact]
    public async Task NotifyAsync_WithNullUrl_DoesNothing()
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        var notifier = new CallbackNotifier(httpFactory.Object, NullLogger<CallbackNotifier>.Instance);

        await notifier.NotifyAsync(null!, new IngestionResult { RequestId = "r1" });

        httpFactory.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task NotifyAsync_WithEmptyUrl_DoesNothing()
    {
        var httpFactory = new Mock<IHttpClientFactory>();
        var notifier = new CallbackNotifier(httpFactory.Object, NullLogger<CallbackNotifier>.Instance);

        await notifier.NotifyAsync("", new IngestionResult { RequestId = "r1" });

        httpFactory.Verify(x => x.CreateClient(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task NotifyAsync_WithValidUrl_PostsResult()
    {
        var handler = new TestHttpMessageHandler(req =>
        {
            req.RequestUri!.ToString().Should().Be("https://callback.example.com/result");
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = new HttpClient(handler);
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);

        var notifier = new CallbackNotifier(httpFactory.Object, NullLogger<CallbackNotifier>.Instance);

        var result = new IngestionResult
        {
            RequestId = "r1",
            CorrelationId = "c1",
            Status = "completed",
            ExternalId = "EXT-001"
        };

        await notifier.NotifyAsync("https://callback.example.com/result", result);

        handler.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task NotifyAsync_WhenHttpFails_DoesNotThrow()
    {
        var handler = new TestHttpMessageHandler(_ => throw new HttpRequestException("Network error"));
        var client = new HttpClient(handler);
        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(client);

        var notifier = new CallbackNotifier(httpFactory.Object, NullLogger<CallbackNotifier>.Instance);

        var act = async () => await notifier.NotifyAsync("https://callback.example.com/result", new IngestionResult { RequestId = "r1" });

        await act.Should().NotThrowAsync();
    }

    private class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public int CallCount { get; private set; }

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_handler(request));
        }
    }
}
