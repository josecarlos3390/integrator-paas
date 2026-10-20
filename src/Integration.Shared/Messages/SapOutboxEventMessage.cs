using System;

namespace Integration.Shared.Messages;

/// <summary>
/// Immutable copy of a SAP HANA outbox event, published to RabbitMQ so other
/// consumers (for example the WhatsApp chatbot) can react to SAP changes
/// without polling HANA directly.
/// </summary>
public class SapOutboxEventMessage
{
    public string EventId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public string? Payload { get; set; }
}
