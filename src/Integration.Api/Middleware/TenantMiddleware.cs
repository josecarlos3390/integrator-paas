namespace Integration.Api.Middleware;

/// <summary>
/// Middleware that sets the current TenantId as a property
/// in all Serilog logs for the current request.
/// </summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var tenantId = context.Items["TenantId"]?.ToString() ?? "unknown";
        using (Serilog.Context.LogContext.PushProperty("TenantId", tenantId))
        {
            await _next(context);
        }
    }
}
