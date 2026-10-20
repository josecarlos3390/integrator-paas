using Integration.Shared.Configuration;
using Integration.Shared.Messages;
using Integration.Shared.Repositories;
using MassTransit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integration.Shared.Services;

/// <summary>
/// Publishes a durable, normalized copy of every HANA outbox event to RabbitMQ.
/// </summary>
public class SapEventPublisher : ISapEventPublisher
{
    private readonly IPublishEndpoint _publishEndpoint;
    private readonly ISapDomainEventFactory _factory;
    private readonly OutboxConfig _config;
    private readonly ILogger<SapEventPublisher> _logger;

    public SapEventPublisher(
        IPublishEndpoint publishEndpoint,
        ISapDomainEventFactory factory,
        IOptions<OutboxConfig> config,
        ILogger<SapEventPublisher> logger)
    {
        _publishEndpoint = publishEndpoint;
        _factory = factory;
        _config = config.Value;
        _logger = logger;
    }

    public async Task PublishAsync(HanaOutboxEvent evt, CancellationToken ct = default)
    {
        if (_config.PublishObjectTypes.Count > 0 &&
            !_config.PublishObjectTypes.Contains(evt.ObjectType))
        {
            return;
        }
        var domain = await _factory.BuildAsync(evt, ct);
        if (domain is null)
        {
            _logger.LogWarning(
                "Skipping SAP event {EventId}: normalization failed", evt.Id);
            return;
        }

        await _publishEndpoint.Publish(domain, ct);

        _logger.LogInformation(
            "Published normalized SAP event {EventId} ({ObjectType}/{EventType}) to sap.events",
            evt.Id, evt.ObjectType, evt.EventType);
    }
}
