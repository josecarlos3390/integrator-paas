using Integration.Shared.Messages;
using MassTransit;

namespace Integration.Worker.Workers;

/// <summary>
/// Example second consumer. It proves that sap.events is a fan-out bus:
/// the chatbot and CRM each get their own durable queue and copy.
/// </summary>
public class CrmSapEventWorker : IConsumer<SapDomainEvent>
{
    private readonly ILogger<CrmSapEventWorker> _logger;

    public CrmSapEventWorker(ILogger<CrmSapEventWorker> logger)
    {
        _logger = logger;
    }

    public Task Consume(ConsumeContext<SapDomainEvent> context)
    {
        // RabbitMQ may keep a legacy catch-all binding; enforce the CRM
        // subscription here until the queue is recreated cleanly.
        if (context.Message.ObjectType is not ("2" or "13"))
            return Task.CompletedTask;

        _logger.LogInformation(
            "CRM received SAP event {EventId} tenant={Tenant} type={ObjectType}/{EventType}",
            context.Message.EventId,
            context.Message.TenantId,
            context.Message.ObjectType,
            context.Message.EventType);
        return Task.CompletedTask;
    }
}
