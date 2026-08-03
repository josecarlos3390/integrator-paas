using System.Collections.Concurrent;

namespace Integration.Shared.Observability;

/// <summary>
/// In-memory snapshot of business metrics for real-time dashboard consumption.
/// Thread-safe and failure-safe. Resets on process restart (counters are ephemeral).
/// Call <see cref="CaptureDeltas"/> periodically and flush to PostgreSQL so the API can read them.
/// </summary>
public static class MetricsSnapshot
{
    private static long _totalEventsProcessed;
    private static long _totalDeadLetters;
    private static long _totalRetries;
    private static long _totalCircuitBreakerChanges;
    private static long _totalTokenRefreshes;
    private static long _totalFeatureFlagDecisions;

    private static readonly ConcurrentDictionary<string, long> _eventsByStatus = new();
    private static readonly ConcurrentDictionary<string, long> _eventsByType = new();
    private static readonly ConcurrentDictionary<string, long> _deadLettersByCategory = new();
    private static readonly ConcurrentDictionary<string, long> _retriesByType = new();
    private static readonly ConcurrentDictionary<string, long> _circuitBySystem = new();
    private static readonly ConcurrentDictionary<string, long> _tokensByTenant = new();

    private static readonly ConcurrentDictionary<string, double> _lastFlushed = new();
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<double>> _latencyWindows = new();
    private const int MaxLatencySamples = 1000;

    public static void RecordEventProcessed(string eventType, string tenantId, string status)
    {
        Interlocked.Increment(ref _totalEventsProcessed);
        _eventsByStatus.AddOrUpdate(status, 1, (_, v) => v + 1);
        _eventsByType.AddOrUpdate(eventType, 1, (_, v) => v + 1);
    }

    public static void RecordEventLatency(string eventType, string tenantId, double durationMs)
    {
        var window = _latencyWindows.GetOrAdd(eventType, _ => new ConcurrentQueue<double>());
        window.Enqueue(durationMs);
        while (window.Count > MaxLatencySamples) window.TryDequeue(out _);
    }

    public static void RecordDeadLetter(string eventType, string tenantId, string errorCategory)
    {
        Interlocked.Increment(ref _totalDeadLetters);
        _deadLettersByCategory.AddOrUpdate(errorCategory, 1, (_, v) => v + 1);
    }

    public static void RecordRetry(string eventType, string tenantId, int attemptNumber)
    {
        Interlocked.Increment(ref _totalRetries);
        _retriesByType.AddOrUpdate(eventType, 1, (_, v) => v + 1);
    }

    public static void RecordCircuitBreakerChange(string targetSystem, string tenantId, string newState)
    {
        Interlocked.Increment(ref _totalCircuitBreakerChanges);
        _circuitBySystem.AddOrUpdate(targetSystem, 1, (_, v) => v + 1);
    }

    public static void RecordTokenRefresh(string tenantId, string reason)
    {
        Interlocked.Increment(ref _totalTokenRefreshes);
        _tokensByTenant.AddOrUpdate(tenantId, 1, (_, v) => v + 1);
    }

    public static void RecordFeatureFlagDecision(string tenantId, string featureKey, bool enabled)
    {
        Interlocked.Increment(ref _totalFeatureFlagDecisions);
    }

    /// <summary>
    /// Calculates deltas since the last flush so they can be persisted to PostgreSQL.
    /// </summary>
    public static Dictionary<string, long> CaptureDeltas()
    {
        var deltas = new Dictionary<string, long>();

        CaptureDelta(deltas, "total_events_processed", Interlocked.Read(ref _totalEventsProcessed));
        CaptureDelta(deltas, "total_dead_letters", Interlocked.Read(ref _totalDeadLetters));
        CaptureDelta(deltas, "total_retries", Interlocked.Read(ref _totalRetries));
        CaptureDelta(deltas, "total_circuit_breaker_changes", Interlocked.Read(ref _totalCircuitBreakerChanges));
        CaptureDelta(deltas, "total_token_refreshes", Interlocked.Read(ref _totalTokenRefreshes));
        CaptureDelta(deltas, "total_feature_flag_decisions", Interlocked.Read(ref _totalFeatureFlagDecisions));

        foreach (var kvp in _eventsByStatus) CaptureDelta(deltas, $"events_status:{kvp.Key}", kvp.Value);
        foreach (var kvp in _eventsByType) CaptureDelta(deltas, $"events_type:{kvp.Key}", kvp.Value);
        foreach (var kvp in _deadLettersByCategory) CaptureDelta(deltas, $"dead_letter_category:{kvp.Key}", kvp.Value);
        foreach (var kvp in _retriesByType) CaptureDelta(deltas, $"retries_type:{kvp.Key}", kvp.Value);
        foreach (var kvp in _circuitBySystem) CaptureDelta(deltas, $"circuit_system:{kvp.Key}", kvp.Value);
        foreach (var kvp in _tokensByTenant) CaptureDelta(deltas, $"token_refresh_tenant:{kvp.Key}", kvp.Value);

        return deltas;
    }

    private static void CaptureDelta(Dictionary<string, long> deltas, string key, long current)
    {
        var last = _lastFlushed.GetOrAdd(key, 0);
        var delta = current - (long)last;
        if (delta > 0)
        {
            deltas[key] = delta;
            _lastFlushed[key] = current;
        }
    }

    /// <summary>
    /// Builds a summary from the raw in-memory counters (useful for single-process scenarios
    /// or after loading persisted values back from the DB).
    /// </summary>
    public static MetricsSummary GetSummary()
    {
        return new MetricsSummary
        {
            TotalEventsProcessed = Interlocked.Read(ref _totalEventsProcessed),
            TotalDeadLetters = Interlocked.Read(ref _totalDeadLetters),
            TotalRetries = Interlocked.Read(ref _totalRetries),
            TotalCircuitBreakerChanges = Interlocked.Read(ref _totalCircuitBreakerChanges),
            TotalTokenRefreshes = Interlocked.Read(ref _totalTokenRefreshes),
            TotalFeatureFlagDecisions = Interlocked.Read(ref _totalFeatureFlagDecisions),
            EventsByStatus = new Dictionary<string, long>(_eventsByStatus),
            EventsByType = new Dictionary<string, long>(_eventsByType),
            DeadLettersByCategory = new Dictionary<string, long>(_deadLettersByCategory),
            RetriesByType = new Dictionary<string, long>(_retriesByType),
            CircuitBreakerBySystem = new Dictionary<string, long>(_circuitBySystem),
            TokenRefreshesByTenant = new Dictionary<string, long>(_tokensByTenant),
            LatencySummary = ComputeLatencySummary()
        };
    }

    private static Dictionary<string, LatencyStats> ComputeLatencySummary()
    {
        var result = new Dictionary<string, LatencyStats>();
        foreach (var kvp in _latencyWindows)
        {
            var values = kvp.Value.ToArray();
            if (values.Length == 0) continue;
            Array.Sort(values);
            result[kvp.Key] = new LatencyStats
            {
                Count = values.Length,
                Min = Math.Round(values[0], 2),
                Max = Math.Round(values[^1], 2),
                P50 = Math.Round(Percentile(values, 0.5), 2),
                P95 = Math.Round(Percentile(values, 0.95), 2),
                P99 = Math.Round(Percentile(values, 0.99), 2),
                Avg = Math.Round(values.Average(), 2)
            };
        }
        return result;
    }

    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1) return sorted[0];
        var idx = (int)Math.Ceiling(sorted.Length * p) - 1;
        return sorted[Math.Clamp(idx, 0, sorted.Length - 1)];
    }
}

public class MetricsSummary
{
    public long TotalEventsProcessed { get; set; }
    public long TotalDeadLetters { get; set; }
    public long TotalRetries { get; set; }
    public long TotalCircuitBreakerChanges { get; set; }
    public long TotalTokenRefreshes { get; set; }
    public long TotalFeatureFlagDecisions { get; set; }
    public Dictionary<string, long> EventsByStatus { get; set; } = new();
    public Dictionary<string, long> EventsByType { get; set; } = new();
    public Dictionary<string, long> DeadLettersByCategory { get; set; } = new();
    public Dictionary<string, long> RetriesByType { get; set; } = new();
    public Dictionary<string, long> CircuitBreakerBySystem { get; set; } = new();
    public Dictionary<string, long> TokenRefreshesByTenant { get; set; } = new();
    public Dictionary<string, LatencyStats> LatencySummary { get; set; } = new();
    public int WindowHours { get; set; }
}

public class LatencyStats
{
    public int Count { get; set; }
    public double Min { get; set; }
    public double Max { get; set; }
    public double Avg { get; set; }
    public double P50 { get; set; }
    public double P95 { get; set; }
    public double P99 { get; set; }
}
