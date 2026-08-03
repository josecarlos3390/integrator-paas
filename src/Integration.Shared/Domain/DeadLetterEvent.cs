namespace Integration.Shared.Domain;

/// <summary>
/// Copy of an event that has exceeded the maximum number of retries.
/// Allows analysis and manual re-enqueuing from the admin portal.
/// </summary>
public class DeadLetterEvent
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty; // "hana_outbox" | "rabbitmq"
    public string EventType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }
    public int AttemptCount { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime DeadLetteredAt { get; set; }
}
