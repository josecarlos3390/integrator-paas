using System.Diagnostics.Metrics;

namespace Integration.Shared.Observability;

/// <summary>
/// Business-level metrics for the integration bus.
/// All methods are failure-safe: exceptions during recording are swallowed
/// so they never affect the main processing flow.
/// </summary>
public static class IntegrationMetrics
{
    private static readonly Meter s_meter = new("Integration.Bus", "1.0");

    // Counters
    private static readonly Counter<long> s_eventsProcessed =
        s_meter.CreateCounter<long>("integration.events.processed", "events", "Total events processed by type, tenant and status");

    private static readonly Counter<long> s_deadLetterEvents =
        s_meter.CreateCounter<long>("integration.dead_letter.events", "events", "Total events sent to dead letter queue");

    private static readonly Counter<long> s_retries =
        s_meter.CreateCounter<long>("integration.events.retries", "retries", "Total retry attempts per event type and tenant");

    private static readonly Counter<long> s_circuitBreakerChanges =
        s_meter.CreateCounter<long>("integration.circuit_breaker.changes", "changes", "Circuit breaker state changes (open/closed)");

    private static readonly Counter<long> s_tokenRefreshes =
        s_meter.CreateCounter<long>("integration.hansa.token_refresh", "refreshes", "HansaCRM OAuth2 token refreshes");

    private static readonly Counter<long> s_featureFlagDecisions =
        s_meter.CreateCounter<long>("integration.feature_flag.decisions", "decisions", "Feature flag enable/disable decisions");

    // Histograms
    private static readonly Histogram<double> s_eventLatency =
        s_meter.CreateHistogram<double>("integration.events.latency_ms", "ms", "End-to-end latency from HANA outbox to CRM response");

    // ------------------------------------------------------------------
    // Public API (all methods are guarded against exceptions)
    // ------------------------------------------------------------------

    public static void RecordEventProcessed(string eventType, string tenantId, string status)
    {
        MetricsSnapshot.RecordEventProcessed(eventType, tenantId, status);
        try
        {
            s_eventsProcessed.Add(1,
                new KeyValuePair<string, object?>("event_type", eventType),
                new KeyValuePair<string, object?>("tenant_id", tenantId),
                new KeyValuePair<string, object?>("status", status));
        }
        catch { /* Metrics must never throw */ }
    }

    public static void RecordEventLatency(string eventType, string tenantId, double durationMs)
    {
        MetricsSnapshot.RecordEventLatency(eventType, tenantId, durationMs);
        try
        {
            s_eventLatency.Record(durationMs,
                new KeyValuePair<string, object?>("event_type", eventType),
                new KeyValuePair<string, object?>("tenant_id", tenantId));
        }
        catch { /* Metrics must never throw */ }
    }

    public static void RecordDeadLetter(string eventType, string tenantId, string errorCategory)
    {
        MetricsSnapshot.RecordDeadLetter(eventType, tenantId, errorCategory);
        try
        {
            s_deadLetterEvents.Add(1,
                new KeyValuePair<string, object?>("event_type", eventType),
                new KeyValuePair<string, object?>("tenant_id", tenantId),
                new KeyValuePair<string, object?>("error_category", errorCategory));
        }
        catch { /* Metrics must never throw */ }
    }

    public static void RecordRetry(string eventType, string tenantId, int attemptNumber)
    {
        MetricsSnapshot.RecordRetry(eventType, tenantId, attemptNumber);
        try
        {
            s_retries.Add(1,
                new KeyValuePair<string, object?>("event_type", eventType),
                new KeyValuePair<string, object?>("tenant_id", tenantId),
                new KeyValuePair<string, object?>("attempt_number", attemptNumber.ToString()));
        }
        catch { /* Metrics must never throw */ }
    }

    public static void RecordCircuitBreakerChange(string targetSystem, string tenantId, string newState)
    {
        MetricsSnapshot.RecordCircuitBreakerChange(targetSystem, tenantId, newState);
        try
        {
            s_circuitBreakerChanges.Add(1,
                new KeyValuePair<string, object?>("target_system", targetSystem),
                new KeyValuePair<string, object?>("tenant_id", tenantId),
                new KeyValuePair<string, object?>("state", newState));
        }
        catch { /* Metrics must never throw */ }
    }

    public static void RecordTokenRefresh(string tenantId, string reason)
    {
        MetricsSnapshot.RecordTokenRefresh(tenantId, reason);
        try
        {
            s_tokenRefreshes.Add(1,
                new KeyValuePair<string, object?>("tenant_id", tenantId),
                new KeyValuePair<string, object?>("reason", reason));
        }
        catch { /* Metrics must never throw */ }
    }

    public static void RecordFeatureFlagDecision(string tenantId, string featureKey, bool enabled)
    {
        MetricsSnapshot.RecordFeatureFlagDecision(tenantId, featureKey, enabled);
        try
        {
            s_featureFlagDecisions.Add(1,
                new KeyValuePair<string, object?>("tenant_id", tenantId),
                new KeyValuePair<string, object?>("feature_key", featureKey),
                new KeyValuePair<string, object?>("result", enabled ? "enabled" : "disabled"));
        }
        catch { /* Metrics must never throw */ }
    }
}
