using Integration.Shared.Infrastructure;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Integration.Api.HealthChecks;

/// <summary>
/// Health check that verifies connectivity to PostgreSQL.
/// </summary>
public class PostgresHealthCheck : IHealthCheck
{
    private readonly IntegrationDbContext _dbContext;
    private readonly ILogger<PostgresHealthCheck> _logger;

    public PostgresHealthCheck(IntegrationDbContext dbContext, ILogger<PostgresHealthCheck> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await _dbContext.Database.CanConnectAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL connection OK");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "PostgreSQL health check failed");
            return HealthCheckResult.Unhealthy("PostgreSQL connection failed", ex);
        }
    }
}
