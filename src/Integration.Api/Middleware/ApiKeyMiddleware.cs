using Integration.Shared.Repositories;

namespace Integration.Api.Middleware;

/// <summary>
/// API Key-based authentication middleware for requests from the external CRM.
/// Reads the X-Api-Key header, validates it against tenant_config and sets
/// TenantId in HttpContext.Items for downstream use.
/// </summary>
public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, TenantConfigRepository tenantRepo)
    {
        // Public endpoints (health) do not require API Key
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        if (path.StartsWith("/health") || path.StartsWith("/swagger") || path.StartsWith("/api/test") || path.StartsWith("/api/mock") || path.StartsWith("/api/crm") || path.StartsWith("/api/dashboard") || path.StartsWith("/api/admin"))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue("X-Api-Key", out var apiKeyHeader))
        {
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Missing X-Api-Key header");
            return;
        }

        var apiKey = apiKeyHeader.ToString();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(apiKey)));

        var tenant = await tenantRepo.GetByApiKeyHashAsync(hash, context.RequestAborted);
        if (tenant == null)
        {
            _logger.LogWarning("Invalid API Key attempted from {RemoteIp}", context.Connection.RemoteIpAddress);
            context.Response.StatusCode = 401;
            await context.Response.WriteAsync("Invalid API Key");
            return;
        }

        context.Items["TenantId"] = tenant.TenantId;
        await _next(context);
    }
}
