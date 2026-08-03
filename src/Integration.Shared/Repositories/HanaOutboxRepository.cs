using System.Data.Odbc;
using Dapper;
using Integration.Shared.Configuration;
using Integration.Shared.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integration.Shared.Repositories;

/// <summary>
/// Read and write of the OUTBOX_EVENTS table in SAP HANA (schema INTEGRATION_BUS).
/// Uses ODBC + Dapper to avoid depending on the proprietary SAP HANA provider for EF Core.
/// Leverages HanaConnectionPool to avoid opening/closing TCP connections per query.
/// </summary>
public class HanaOutboxRepository
{
    private readonly HanaConnectionPool _pool;
    private readonly ILogger<HanaOutboxRepository> _logger;
    private readonly Dictionary<string, int> _priorityMap;

    public HanaOutboxRepository(
        HanaConnectionPool pool,
        IOptions<OutboxConfig> outboxConfig,
        ILogger<HanaOutboxRepository> logger)
    {
        _pool = pool;
        _logger = logger;
        _priorityMap = outboxConfig.Value.EventPriority;
    }

    /// <summary>
    /// Reads a batch of unprocessed (and not dead-letter) events ordered by date.
    /// Only returns events that are not currently leased or whose lease has expired.
    /// </summary>
    public async Task<IReadOnlyList<HanaOutboxEvent>> FetchPendingAsync(int batchSize, int maxAttempts, CancellationToken ct = default)
    {
        var fetchSize = Math.Max(batchSize * 3, 50);

        const string sql = @"
            SELECT 
                ID,
                TENANT_ID AS TenantId,
                EVENT_TYPE AS EventType,
                OBJECT_TYPE AS ObjectType,
                AGGREGATE_ID AS AggregateId,
                OCCURRED_AT AS OccurredAt,
                PROCESSED_AT AS ProcessedAt,
                ATTEMPT_COUNT AS AttemptCount,
                ERROR_MESSAGE AS ErrorMessage,
                PAYLOAD AS Payload,
                IS_DEAD_LETTER AS IsDeadLetter,
                LEASED_UNTIL AS LeasedUntil
            FROM INTEGRATION_BUS.OUTBOX_EVENTS
            WHERE PROCESSED_AT IS NULL
              AND IS_DEAD_LETTER = 0
              AND ATTEMPT_COUNT < ?
              AND (LEASED_UNTIL IS NULL OR LEASED_UNTIL < CURRENT_TIMESTAMP)
            ORDER BY OCCURRED_AT ASC
            LIMIT ?;
        ";

        using var lease = await _pool.AcquireAsync(ct);
        var results = (await lease.Connection.QueryAsync<HanaOutboxEvent>(sql, new { MaxAttempts = maxAttempts, BatchSize = fetchSize })).ToList();

        foreach (var evt in results)
        {
            evt.Priority = _priorityMap.GetValueOrDefault(evt.ObjectType, 0);
        }

        return results
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.OccurredAt)
            .Take(batchSize)
            .ToList();
    }

    public virtual async Task MarkProcessedAsync(string id, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE INTEGRATION_BUS.OUTBOX_EVENTS
            SET PROCESSED_AT = CURRENT_TIMESTAMP,
                ERROR_MESSAGE = NULL,
                LEASED_UNTIL = NULL
            WHERE ID = ?;
        ";

        using var lease = await _pool.AcquireAsync(ct);
        await lease.Connection.ExecuteAsync(sql, new { Id = id });
    }

    public async Task MarkFailedAsync(string id, string errorMessage, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE INTEGRATION_BUS.OUTBOX_EVENTS
            SET ATTEMPT_COUNT = ATTEMPT_COUNT + 1,
                ERROR_MESSAGE = ?,
                LEASED_UNTIL = NULL
            WHERE ID = ?;
        ";

        using var lease = await _pool.AcquireAsync(ct);
        await lease.Connection.ExecuteAsync(sql, new { ErrorMessage = Truncate(errorMessage, 4000), Id = id });
    }

    public async Task MarkDeadLetterAsync(string id, string errorMessage, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE INTEGRATION_BUS.OUTBOX_EVENTS
            SET IS_DEAD_LETTER = 1,
                ERROR_MESSAGE = ?,
                LEASED_UNTIL = NULL
            WHERE ID = ?;
        ";

        using var lease = await _pool.AcquireAsync(ct);
        await lease.Connection.ExecuteAsync(sql, new { ErrorMessage = Truncate(errorMessage, 4000), Id = id });
    }

    /// <summary>
    /// Acquires a time-based lease on a set of event IDs to prevent concurrent processing.
    /// Uses a single batch UPDATE with IN clause instead of N+1 round-trips.
    /// Falls back to individual updates if the batch is a single ID (ODBC compatibility).
    /// </summary>
    public async Task<IReadOnlyList<string>> AcquireLeaseAsync(IEnumerable<string> ids, TimeSpan duration, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return Array.Empty<string>();

        var leasedUntil = DateTime.UtcNow.Add(duration);

        using var lease = await _pool.AcquireAsync(ct);
        var conn = lease.Connection;

        if (idList.Count == 1)
        {
            const string singleSql = @"
                UPDATE INTEGRATION_BUS.OUTBOX_EVENTS
                SET LEASED_UNTIL = ?
                WHERE ID = ?
                  AND (LEASED_UNTIL IS NULL OR LEASED_UNTIL < CURRENT_TIMESTAMP);";

            var affected = await conn.ExecuteAsync(singleSql, new object[] { leasedUntil, idList[0] });
            return affected > 0 ? idList : Array.Empty<string>();
        }

        // Batch UPDATE using IN clause with positional parameters for HANA ODBC
        var placeholders = string.Join(", ", idList.Select(_ => "?"));
        var updateSql = $@"
            UPDATE INTEGRATION_BUS.OUTBOX_EVENTS
            SET LEASED_UNTIL = ?
            WHERE ID IN ({placeholders})
              AND (LEASED_UNTIL IS NULL OR LEASED_UNTIL < CURRENT_TIMESTAMP);";

        var updateParams = new List<object> { leasedUntil };
        updateParams.AddRange(idList);
        var affectedTotal = await conn.ExecuteAsync(updateSql, updateParams);

        if (affectedTotal == idList.Count)
            return idList;

        // Partial lease: determine which IDs were actually updated
        var selectSql = $@"
            SELECT ID FROM INTEGRATION_BUS.OUTBOX_EVENTS
            WHERE ID IN ({placeholders})
              AND LEASED_UNTIL = ?;";

        var selectParams = new List<object>();
        selectParams.AddRange(idList);
        selectParams.Add(leasedUntil);

        var leasedIds = await conn.QueryAsync<string>(selectSql, selectParams);
        return leasedIds.ToList();
    }

    /// <summary>
    /// Delays an event by extending its lease without incrementing the attempt count.
    /// Use this when a tenant quota is exceeded to retry later without penalizing the event.
    /// </summary>
    public virtual async Task DelayEventAsync(string id, TimeSpan delay, CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE INTEGRATION_BUS.OUTBOX_EVENTS
            SET LEASED_UNTIL = ?
            WHERE ID = ?;
        ";

        using var lease = await _pool.AcquireAsync(ct);
        var leasedUntil = DateTime.UtcNow.Add(delay);
        await lease.Connection.ExecuteAsync(sql, new object[] { leasedUntil, id });
    }

    public async Task ResetForRetryAsync(string aggregateId, CancellationToken ct = default)
    {
        const string selectSql = @"
            SELECT ID FROM INTEGRATION_BUS.OUTBOX_EVENTS
            WHERE AGGREGATE_ID = ? AND IS_DEAD_LETTER = 1
            ORDER BY OCCURRED_AT DESC
            LIMIT 1;
        ";

        const string updateSql = @"
            UPDATE INTEGRATION_BUS.OUTBOX_EVENTS
            SET IS_DEAD_LETTER = 0,
                ATTEMPT_COUNT = 0,
                ERROR_MESSAGE = NULL,
                PROCESSED_AT = NULL
            WHERE ID = ?;
        ";

        using var lease = await _pool.AcquireAsync(ct);
        var id = await lease.Connection.ExecuteScalarAsync<string>(selectSql, new { AggregateId = aggregateId });
        if (id != null)
        {
            await lease.Connection.ExecuteAsync(updateSql, new { Id = id });
        }
    }

    public async Task InsertPriceListEventAsync(
        string tenantId,
        string aggregateId,
        string payload,
        CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        const string sql = @"
            INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS 
            (ID, TENANT_ID, EVENT_TYPE, OBJECT_TYPE, AGGREGATE_ID, PAYLOAD, OCCURRED_AT, PROCESSED_AT, ATTEMPT_COUNT, ERROR_MESSAGE, IS_DEAD_LETTER)
            VALUES 
            (?, ?, 'PriceListChanged', 'PRICE_LIST', ?, ?, CURRENT_TIMESTAMP, NULL, 0, NULL, 0);
        ";

        using var lease = await _pool.AcquireAsync(ct);
        await lease.Connection.ExecuteAsync(sql, new { Id = id, TenantId = tenantId, AggregateId = aggregateId, Payload = payload });
    }

    public async Task InsertPriceListHeaderEventAsync(
        string tenantId,
        string aggregateId,
        string payload,
        CancellationToken ct = default)
    {
        var id = Guid.NewGuid().ToString();
        const string sql = @"
            INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS 
            (ID, TENANT_ID, EVENT_TYPE, OBJECT_TYPE, AGGREGATE_ID, PAYLOAD, OCCURRED_AT, PROCESSED_AT, ATTEMPT_COUNT, ERROR_MESSAGE, IS_DEAD_LETTER)
            VALUES 
            (?, ?, 'PriceListHeaderChanged', 'PRICE_LIST_HEADER', ?, ?, CURRENT_TIMESTAMP, NULL, 0, NULL, 0);
        ";

        using var lease = await _pool.AcquireAsync(ct);
        await lease.Connection.ExecuteAsync(sql, new { Id = id, TenantId = tenantId, AggregateId = aggregateId, Payload = payload });
    }

    public async Task<(IReadOnlyList<HanaOutboxEvent> Items, int TotalCount)> FetchAllAsync(
        string? eventType = null,
        string? objectType = null,
        string? status = null,
        int skip = 0,
        int take = 25,
        CancellationToken ct = default)
    {
        var whereClauses = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            whereClauses.Add("EVENT_TYPE = ?");
            parameters.Add("EventType", eventType);
        }

        if (!string.IsNullOrWhiteSpace(objectType))
        {
            whereClauses.Add("OBJECT_TYPE = ?");
            parameters.Add("ObjectType", objectType);
        }

        if (status == "pending")
            whereClauses.Add("PROCESSED_AT IS NULL AND IS_DEAD_LETTER = 0");
        else if (status == "processed")
            whereClauses.Add("PROCESSED_AT IS NOT NULL");
        else if (status == "dead_letter")
            whereClauses.Add("IS_DEAD_LETTER = 1");
        else if (status == "failed")
            whereClauses.Add("PROCESSED_AT IS NULL AND IS_DEAD_LETTER = 0 AND ATTEMPT_COUNT > 0");

        var whereSql = whereClauses.Count > 0 ? "WHERE " + string.Join(" AND ", whereClauses) : "";

        var countSql = $"SELECT COUNT(*) FROM INTEGRATION_BUS.OUTBOX_EVENTS {whereSql}";
        var dataSql = $@"
            SELECT 
                ID,
                TENANT_ID AS TenantId,
                EVENT_TYPE AS EventType,
                OBJECT_TYPE AS ObjectType,
                AGGREGATE_ID AS AggregateId,
                OCCURRED_AT AS OccurredAt,
                PROCESSED_AT AS ProcessedAt,
                ATTEMPT_COUNT AS AttemptCount,
                ERROR_MESSAGE AS ErrorMessage,
                IS_DEAD_LETTER AS IsDeadLetter
            FROM INTEGRATION_BUS.OUTBOX_EVENTS
            {whereSql}
            ORDER BY OCCURRED_AT DESC
            LIMIT ? OFFSET ?;
        ";

        using var lease = await _pool.AcquireAsync(ct);
        var totalCount = await lease.Connection.ExecuteScalarAsync<int>(countSql, parameters);

        parameters.Add("Take", take);
        parameters.Add("Skip", skip);
        var results = await lease.Connection.QueryAsync<HanaOutboxEvent>(dataSql, parameters);
        return (results.ToList(), totalCount);
    }

    public async Task<HanaOutboxStats> FetchStatsAsync(CancellationToken ct = default)
    {
        const string totalSql = "SELECT COUNT(*) FROM INTEGRATION_BUS.OUTBOX_EVENTS;";
        const string processedSql = "SELECT COUNT(*) FROM INTEGRATION_BUS.OUTBOX_EVENTS WHERE PROCESSED_AT IS NOT NULL;";
        const string deadLetterSql = "SELECT COUNT(*) FROM INTEGRATION_BUS.OUTBOX_EVENTS WHERE IS_DEAD_LETTER = 1;";
        const string failedSql = @"
            SELECT COUNT(*) FROM INTEGRATION_BUS.OUTBOX_EVENTS
            WHERE PROCESSED_AT IS NULL AND IS_DEAD_LETTER = 0 AND ATTEMPT_COUNT > 0;
        ";
        const string pendingSql = @"
            SELECT COUNT(*) FROM INTEGRATION_BUS.OUTBOX_EVENTS
            WHERE PROCESSED_AT IS NULL AND IS_DEAD_LETTER = 0 AND ATTEMPT_COUNT = 0;
        ";

        using var lease = await _pool.AcquireAsync(ct);
        var conn = lease.Connection;

        var total = await conn.ExecuteScalarAsync<int>(totalSql);
        var processed = await conn.ExecuteScalarAsync<int>(processedSql);
        var deadLetter = await conn.ExecuteScalarAsync<int>(deadLetterSql);
        var failed = await conn.ExecuteScalarAsync<int>(failedSql);
        var pending = await conn.ExecuteScalarAsync<int>(pendingSql);

        return new HanaOutboxStats
        {
            Total = total,
            Processed = processed,
            DeadLetter = deadLetter,
            Failed = failed,
            Pending = pending
        };
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}

public class HanaOutboxStats
{
    public int Total { get; set; }
    public int Processed { get; set; }
    public int DeadLetter { get; set; }
    public int Failed { get; set; }
    public int Pending { get; set; }
}

public class HanaOutboxEvent
{
    public string Id { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string ObjectType { get; set; } = string.Empty;
    public string AggregateId { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public int AttemptCount { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Payload { get; set; }
    public int IsDeadLetter { get; set; }
    public DateTime? LeasedUntil { get; set; }
    public int Priority { get; set; }
}
