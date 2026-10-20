using Integration.Shared.Configuration;
using Integration.Shared.Repositories;
using Microsoft.Extensions.Options;

namespace Integration.Api.Middleware;

/// <summary>
/// API Key-based authentication middleware for requests from the external CRM.
/// Reads the X-Api-Key header, validates it against tenant_config and sets
/// TenantId in HttpContext.Items for downstream use. Routes under /api/master
/// require the master administration key (X-Admin-Key) instead.
/// </summary>
public class ApiKeyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;
    private readonly AdminConfig _adminConfig;

    public ApiKeyMiddleware(RequestDelegate next, ILogger<ApiKeyMiddleware> logger, IOptions<AdminConfig> adminConfig)
    {
        _next = next;
        _logger = logger;
        _adminConfig = adminConfig.Value;
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

        // Master administration endpoints require the master key, not a tenant key
        if (path.StartsWith("/api/master"))
        {
            if (!context.Request.Headers.TryGetValue("X-Admin-Key", out var adminKeyHeader))
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Missing X-Admin-Key header");
                return;
            }

            var adminKeyHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(adminKeyHeader.ToString())));
            if (string.IsNullOrEmpty(_adminConfig.MasterApiKeyHash) ||
                !string.Equals(adminKeyHash, _adminConfig.MasterApiKeyHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Invalid master administration key attempted from {RemoteIp}", context.Connection.RemoteIpAddress);
                context.Response.StatusCode = 401;
                await context.Response.WriteAsync("Invalid X-Admin-Key");
                return;
            }

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
