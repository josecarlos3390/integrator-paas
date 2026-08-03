namespace Integration.Shared.Domain;

/// <summary>
/// Represents an outbox event stored in PostgreSQL for reliable
/// publishing through RabbitMQ (Outbox pattern).
/// </summary>
public class OutboxEvent
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int AttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
}
