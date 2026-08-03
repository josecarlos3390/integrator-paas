namespace Integration.Shared.Domain;

/// <summary>
/// Ephemeral runtime metric counter persisted to PostgreSQL so the API
/// can read values written by the Worker (or multiple Worker instances).
/// </summary>
public class IntegrationMetricCounter
{
    public string MetricKey { get; set; } = string.Empty;
    public long MetricValue { get; set; }
    public DateTime UpdatedAt { get; set; }
}
