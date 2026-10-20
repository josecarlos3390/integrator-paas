using Integration.Shared.Messages;
using Integration.Shared.Repositories;

namespace Integration.Shared.Services;

public interface ISapDomainEventFactory
{
    Task<SapDomainEvent?> BuildAsync(
        HanaOutboxEvent evt, CancellationToken ct = default);
}
