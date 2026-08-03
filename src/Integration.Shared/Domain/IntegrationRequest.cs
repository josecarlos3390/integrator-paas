namespace Integration.Shared.Domain;

/// <summary>
/// Represents an inbound integration request received by the Data Ingestor.
/// Stored durably in PostgreSQL before processing.
/// </summary>
public class IntegrationRequest
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public string SourceSystem { get; set; } = string.Empty;
    public string TargetSystem { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string? CallbackUrl { get; set; }
    public string Status { get; set; } = "received";
    public int AttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ResultPayload { get; set; }
    public int Priority { get; set; }
    public DateTime ReceivedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public DateTime? NextRetryAt { get; set; }
    public DateTime? LeasedUntil { get; set; }
}
