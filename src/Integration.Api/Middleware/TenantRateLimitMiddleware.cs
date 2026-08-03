using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace Integration.Api.Middleware;

/// <summary>
/// Per-tenant rate limiting middleware.
/// Each tenant gets its own token bucket (100 requests / minute).
/// Returns 429 when the limit is exceeded.
/// </summary>
public class TenantRateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantRateLimitMiddleware> _logger;

    // Static dictionary so limiters survive across requests (process lifetime).
    private static readonly ConcurrentDictionary<string, TokenBucketRateLimiter> _limiters = new();

    public TenantRateLimitMiddleware(RequestDelegate next, ILogger<TenantRateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var tenantId = context.Items["TenantId"]?.ToString() ?? "anonymous";

        var limiter = _limiters.GetOrAdd(tenantId, _ => new TokenBucketRateLimiter(
            new TokenBucketRateLimiterOptions
            {
                TokenLimit = 100,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 0,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                TokensPerPeriod = 100,
                AutoReplenishment = true
            }));

        using var lease = await limiter.AcquireAsync(1, context.RequestAborted);
        if (!lease.IsAcquired)
        {
            _logger.LogWarning("Rate limit exceeded for tenant {TenantId}", tenantId);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync("Rate limit exceeded. Try again later.", context.RequestAborted);
            return;
        }

        await _next(context);
    }
}
