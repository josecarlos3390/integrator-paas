using Integration.Shared.Configuration;
using Integration.Shared.Domain;
using Integration.Shared.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integration.Shared.Services;

/// <summary>
/// Idempotency implementation with PostgreSQL recording.
/// </summary>
public class IdempotencyService : IIdempotencyService
{
    private readonly IdempotencyRepository _repo;
    private readonly IOptions<IdempotencyConfig> _config;
    private readonly ILogger<IdempotencyService> _logger;

    public IdempotencyService(
        IdempotencyRepository repo,
        IOptions<IdempotencyConfig> config,
        ILogger<IdempotencyService> logger)
    {
        _repo = repo;
        _config = config;
        _logger = logger;
    }

    public async Task<IdempotencyResult> TryProcessAsync(
        string tenantId,
        string eventType,
        string aggregateId,
        Func<Task> processFunc,
        CancellationToken ct = default)
    {
        if (!_config.Value.Enabled)
        {
            await processFunc();
            return IdempotencyResult.Processed;
        }

        var existing = await _repo.GetAsync(tenantId, eventType, aggregateId, ct);
        if (existing?.Status == IdempotencyStatus.Success)
        {
            _logger.LogInformation(
                "Idempotency hit: {EventType} {AggregateId} for tenant {TenantId} was already processed at {ProcessedAt}",
                eventType, aggregateId, tenantId, existing.ProcessedAt);
            return IdempotencyResult.AlreadyProcessed;
        }

        try
        {
            await processFunc();

            await _repo.UpsertAsync(new IdempotencyRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                EventType = eventType,
                AggregateId = aggregateId,
                Status = IdempotencyStatus.Success,
                ProcessedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(_config.Value.TtlDays)
            }, ct);

            return IdempotencyResult.Processed;
        }
        catch (Exception ex) when (IsBusinessError(ex))
        {
            // We do not store business errors to allow manual retry
            _logger.LogWarning(ex,
                "Business error processing {EventType} {AggregateId} for tenant {TenantId}. Not storing idempotency record to allow retry.",
                eventType, aggregateId, tenantId);
            throw;
        }
        catch (Exception)
        {
            // Transient errors: we store them to avoid immediate retry
            // but we allow retry via DLQ/reset because the record can be invalidated
            try
            {
                await _repo.UpsertAsync(new IdempotencyRecord
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    EventType = eventType,
                    AggregateId = aggregateId,
                    Status = IdempotencyStatus.TransientError,
                    ProcessedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(_config.Value.TtlDays)
                }, ct);
            }
            catch (Exception upsertEx)
            {
                _logger.LogError(upsertEx, "Failed to store transient error idempotency record");
            }

            return IdempotencyResult.Failed;
        }
    }

    public async Task InvalidateAsync(
        string tenantId,
        string eventType,
        string aggregateId,
        CancellationToken ct = default)
    {
        var existing = await _repo.GetAsync(tenantId, eventType, aggregateId, ct);
        if (existing is not null)
        {
            existing.Status = IdempotencyStatus.TransientError; // Permite re-proceso
            existing.ExpiresAt = DateTime.UtcNow.AddDays(_config.Value.TtlDays);
            await _repo.UpsertAsync(existing, ct);
            _logger.LogInformation(
                "Idempotency record invalidated for {EventType} {AggregateId} tenant {TenantId}",
                eventType, aggregateId, tenantId);
        }
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken ct = default)
    {
        var deleted = await _repo.DeleteExpiredAsync(DateTime.UtcNow, ct);
        if (deleted > 0)
        {
            _logger.LogInformation("Idempotency cleanup removed {Count} expired records", deleted);
        }
        return deleted;
    }

    private static bool IsBusinessError(Exception ex)
    {
        return ex is Integration.Shared.Exceptions.SapIntegrationException sapEx && sapEx.IsBusinessError;
    }
}
