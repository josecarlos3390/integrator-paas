namespace Integration.Shared.Domain;

/// <summary>
/// Stores identifiers of already processed messages to ensure
/// idempotency in queue consumers and in the outbox dispatcher.
/// </summary>
public class ProcessedMessage
{
    public Guid Id { get; set; }
    public string MessageId { get; set; } = string.Empty;
    public string Consumer { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
}
