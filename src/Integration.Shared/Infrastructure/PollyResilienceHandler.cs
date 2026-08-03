using Polly;

namespace Integration.Shared.Infrastructure;

/// <summary>
/// DelegatingHandler that applies a Polly v8 ResiliencePipeline
/// to all outgoing HTTP requests. Allows sharing retry
/// and circuit breaker between HttpClientFactory and Refit.
/// </summary>
public class PollyResilienceHandler : DelegatingHandler
{
    private readonly ResiliencePipeline<HttpResponseMessage> _pipeline;

    public PollyResilienceHandler(ResiliencePipeline<HttpResponseMessage> pipeline)
    {
        _pipeline = pipeline;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return await _pipeline.ExecuteAsync(async ct => await base.SendAsync(request, ct), cancellationToken);
    }
}
