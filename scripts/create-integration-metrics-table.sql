-- ============================================================================
-- Runtime Metrics Counters
-- ============================================================================
-- Esta tabla persiste contadores en tiempo real escritos por el Worker
-- y leídos por el API para el dashboard. Usa UPSERT (INSERT ... ON CONFLICT)
-- para ser segura con múltiples instancias del Worker.
-- ============================================================================

CREATE TABLE IF NOT EXISTS integration_metric_counters (
    metric_key   VARCHAR(128) PRIMARY KEY,
    metric_value BIGINT NOT NULL DEFAULT 0,
    updated_at   TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_integration_metric_counters_updated
    ON integration_metric_counters (updated_at);
