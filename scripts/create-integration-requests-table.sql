-- Migration: create integration_requests table for the Data Ingestor
-- Run this against PostgreSQL before deploying the new version.

CREATE TABLE IF NOT EXISTS integration_requests (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    tenant_id       VARCHAR(64) NOT NULL,
    correlation_id  VARCHAR(64) NOT NULL,
    source_system   VARCHAR(64) NOT NULL,
    target_system   VARCHAR(64) NOT NULL,
    entity_type     VARCHAR(64) NOT NULL,
    operation       VARCHAR(32) NOT NULL DEFAULT 'create',
    external_id     VARCHAR(128) NOT NULL DEFAULT '',
    payload         TEXT NOT NULL,
    callback_url    TEXT,
    status          VARCHAR(32) NOT NULL DEFAULT 'received',
    attempt_count   INTEGER NOT NULL DEFAULT 0,
    error_message   TEXT,
    result_payload  TEXT,
    priority        INTEGER NOT NULL DEFAULT 0,
    received_at     TIMESTAMPTZ NOT NULL DEFAULT CURRENT_TIMESTAMP,
    processed_at    TIMESTAMPTZ,
    next_retry_at   TIMESTAMPTZ,
    leased_until    TIMESTAMPTZ
);

CREATE INDEX IF NOT EXISTS idx_integration_requests_pending
    ON integration_requests (tenant_id, status, next_retry_at)
    WHERE status IN ('received', 'failed');

CREATE INDEX IF NOT EXISTS idx_integration_requests_external
    ON integration_requests (tenant_id, external_id, entity_type);

CREATE INDEX IF NOT EXISTS idx_integration_requests_lease
    ON integration_requests (status, leased_until);

CREATE INDEX IF NOT EXISTS idx_integration_requests_received
    ON integration_requests (received_at);
