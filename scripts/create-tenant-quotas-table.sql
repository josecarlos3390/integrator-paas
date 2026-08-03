-- ============================================================================
-- Tenant Quotas
-- ============================================================================
-- Prevents a single tenant from overwhelming the integration bus.
-- Default: 1000 events/hour, 100 API calls/minute.
-- ============================================================================

CREATE TABLE IF NOT EXISTS tenant_quotas (
    tenant_id               VARCHAR(64) PRIMARY KEY REFERENCES tenant_config(tenant_id),
    max_events_per_hour     INT NOT NULL DEFAULT 1000,
    max_api_calls_per_minute INT NOT NULL DEFAULT 100,
    updated_at              TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);
