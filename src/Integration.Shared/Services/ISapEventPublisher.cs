using Integration.Shared.Repositories;

namespace Integration.Shared.Services;

/// <summary>
/// Publishes SAP outbox events to the integration event bus.
/// </summary>
public interface ISapEventPublisher
{
    Task PublishAsync(HanaOutboxEvent evt, CancellationToken ct = default);
}
