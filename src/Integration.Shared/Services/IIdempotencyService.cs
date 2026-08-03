namespace Integration.Shared.Services;

/// <summary>
/// Result of an idempotency operation.
/// </summary>
public enum IdempotencyResult
{
    AlreadyProcessed,
    Processed,
    Failed
}

/// <summary>
/// Idempotency service that ensures an event (tenant+type+aggregate)
/// is not processed successfully twice.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Executes <paramref name="processFunc" /> only if no previous success record exists.
    /// The execution result is saved automatically.
    /// </summary>
    Task<IdempotencyResult> TryProcessAsync(
        string tenantId,
        string eventType,
        string aggregateId,
        Func<Task> processFunc,
        CancellationToken ct = default);

    /// <summary>
    /// Invalidates the idempotency record to allow manual re-processing.
    /// </summary>
    Task InvalidateAsync(
        string tenantId,
        string eventType,
        string aggregateId,
        CancellationToken ct = default);

    /// <summary>
    /// Deletes expired records.
    /// </summary>
    Task<int> CleanupExpiredAsync(CancellationToken ct = default);
}
