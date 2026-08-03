using Integration.Shared.Domain;

namespace Integration.Worker.Services;

/// <summary>
/// Routes an integration request to the appropriate handler based on
/// entity type and target system.
/// </summary>
public interface IRequestRouter
{
    /// <summary>
    /// Attempts to route and process the request.
    /// Returns a result payload (e.g., target system ID) on success,
    /// or throws if the route is unsupported or processing fails.
    /// </summary>
    Task<string?> RouteAsync(IntegrationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Returns true if the router can handle this entity+target combination.
    /// </summary>
    bool CanRoute(string entityType, string targetSystem);
}
