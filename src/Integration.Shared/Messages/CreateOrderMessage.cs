using Integration.Shared.Dtos;

namespace Integration.Shared.Messages;

/// <summary>
/// Message enqueued in RabbitMQ for asynchronous processing of CRM→SAP orders.
/// </summary>
public class CreateOrderMessage
{
    public string TenantId { get; set; } = string.Empty;
    public string CorrelationId { get; set; } = string.Empty;
    public CrmOrderPayload Payload { get; set; } = new();
    public string? CallbackUrl { get; set; }
}
