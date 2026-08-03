namespace Integration.Shared.Domain;

/// <summary>
/// Idempotency record status.
/// </summary>
public enum IdempotencyStatus
{
    Success,
    BusinessError,
    TransientError
}

/// <summary>
/// Idempotency record by (TenantId, EventType, AggregateId).
/// Ensures that an event is not processed successfully twice.
/// </summary>
public class IdempotencyRecord
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public IdempotencyStatus Status { get; set; }
    public DateTime ProcessedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
