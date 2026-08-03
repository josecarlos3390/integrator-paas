using Integration.Shared.Domain;

namespace Integration.Shared.Services;

/// <summary>
/// Operational alerting service. Allows raising, querying and acknowledging alerts.
/// </summary>
public interface IAlertingService
{
    /// <summary>
    /// Creates an alert if there is no recent active alert of the same type for the same tenant.
    /// </summary>
    Task RaiseAlertAsync(
        AlertType alertType,
        AlertSeverity severity,
        string tenantId,
        string title,
        string message,
        string? details = null,
        CancellationToken ct = default);

    Task AcknowledgeAlertAsync(Guid alertId, string? acknowledgedBy, CancellationToken ct = default);
    Task<(IReadOnlyList<IntegrationAlert> Items, int TotalCount)> GetActiveAlertsAsync(string? tenantId = null, int skip = 0, int take = 25, CancellationToken ct = default);
    Task<(IReadOnlyList<IntegrationAlert> Items, int TotalCount)> GetRecentAlertsAsync(string? tenantId = null, int skip = 0, int take = 25, CancellationToken ct = default);
    Task<object> GetStatsAsync(CancellationToken ct = default);
}
