using FluentAssertions;
using Integration.Shared.Configuration;
using Integration.Shared.Messages;
using Integration.Shared.Repositories;
using Integration.Shared.Services;
using MassTransit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Integration.Worker.Tests.Services;

public class SapEventPublisherTests
{
    [Fact]
    public async Task PublishAsync_PublishesNormalizedDomainEvent()
    {
        var endpoint = new Mock<IPublishEndpoint>();
        var factory = new Mock<ISapDomainEventFactory>();
        var domain = new SapDomainEvent
        {
            EventId = "evt-1",
            TenantId = "RETAIL_QA5",
            ObjectType = "13",
            AggregateId = "1001"
        };
        factory
            .Setup(x => x.BuildAsync(It.IsAny<HanaOutboxEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(domain);

        var publisher = new SapEventPublisher(
            endpoint.Object,
            factory.Object,
            Options.Create(new OutboxConfig { PublishObjectTypes = new HashSet<string>() }),
            NullLogger<SapEventPublisher>.Instance);

        var evt = new HanaOutboxEvent
        {
            Id = "evt-1",
            TenantId = "RETAIL_QA5",
            EventType = "Created",
            ObjectType = "13",
            AggregateId = "1001",
            OccurredAt = new DateTime(2026, 10, 19, 20, 0, 0, DateTimeKind.Utc),
            Payload = "{}"
        };

        await publisher.PublishAsync(evt);

        endpoint.Verify(
            x => x.Publish(
                It.Is<SapDomainEvent>(m =>
                    m.EventId == "evt-1" &&
                    m.ObjectType == "13" &&
                    m.AggregateId == "1001" &&
                    m.TenantId == "RETAIL_QA5"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
