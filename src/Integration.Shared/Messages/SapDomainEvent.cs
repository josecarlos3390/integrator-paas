using System;

namespace Integration.Shared.Messages;

/// <summary>
/// Normalized event for downstream consumers. SAP-specific keys and
/// Service Layer payloads are resolved by integration-paas before publishing.
/// </summary>
public class SapDomainEvent
{
    public string EventId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string CompanyDb { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public DateTime OccurredAt { get; set; }
    public object? Data { get; set; }
}
