using System.Net;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Integration.Shared.Infrastructure;

/// <summary>
/// Centralized resilience policies with Polly v8.
/// Includes retry with exponential backoff + jitter and circuit breaker.
/// </summary>
public static class ResiliencePolicies
{
    /// <summary>
    /// Pipeline for SAP Service Layer: isolated retry + circuit breaker.
    /// </summary>
    public static ResiliencePipeline<HttpResponseMessage> BuildSapPipeline()
    {
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    // TaskCanceledException = HttpClient.Timeout elapsed (slow/hung Service Layer).
                    // Polly v8 does not retry cancellations tied to the caller's own token.
                    .Handle<TaskCanceledException>()
                    .HandleResult(r =>
                        r.StatusCode == HttpStatusCode.RequestTimeout ||
                        r.StatusCode == HttpStatusCode.TooManyRequests ||
                        (int)r.StatusCode >= 500),
                // Bounded because each attempt can take up to the 90s HttpClient timeout.
                MaxRetryAttempts = 3,
                DelayGenerator = static args =>
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber))
                                + TimeSpan.FromSeconds(Random.Shared.NextDouble());
                    return new ValueTask<TimeSpan?>(delay);
                },
                OnRetry = static args => default
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => (int)r.StatusCode >= 500),
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(60),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
                OnOpened = static args => default,
                OnClosed = static args => default
            })
            .Build();
    }

    /// <summary>
    /// Pipeline for CRM API: isolated retry + circuit breaker.
    /// CRM failures do not affect SAP calls.
    /// </summary>
    public static ResiliencePipeline<HttpResponseMessage> BuildCrmPipeline()
    {
        return new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r =>
                        r.StatusCode == HttpStatusCode.RequestTimeout ||
                        r.StatusCode == HttpStatusCode.TooManyRequests ||
                        (int)r.StatusCode >= 500),
                MaxRetryAttempts = 5,
                DelayGenerator = static args =>
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber))
                                + TimeSpan.FromSeconds(Random.Shared.NextDouble());
                    return new ValueTask<TimeSpan?>(delay);
                },
                OnRetry = static args => default
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>()
                    .HandleResult(r => (int)r.StatusCode >= 500),
                FailureRatio = 0.5,
                SamplingDuration = TimeSpan.FromSeconds(60),
                MinimumThroughput = 5,
                BreakDuration = TimeSpan.FromSeconds(30),
                OnOpened = static args => default,
                OnClosed = static args => default
            })
            .Build();
    }
}
