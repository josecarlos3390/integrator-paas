namespace Integration.Shared.Domain;

/// <summary>
/// Audit record for each integration operation.
/// Allows tracing the full cycle of a transaction.
/// </summary>
public class IntegrationLog
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public IntegrationDirection Direction { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string? ExternalId { get; set; }
    public string? SapDocEntry { get; set; }
    public string Status { get; set; } = string.Empty; // success, error, dead_letter
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum IntegrationDirection
{
    SapToCrm,
    CrmToSap
}
