-- ============================================================================
-- Composite indexes for integration_logs (SaaS scalability, Phase 1)
-- ============================================================================
-- These indexes support the dashboard metrics queries that filter by
-- created_at + status and created_at + tenant_id.
-- Without them, PostgreSQL will seq-scan or filter in-memory on large tables.
-- ============================================================================

CREATE INDEX IF NOT EXISTS idx_integration_logs_created_at_status
    ON integration_logs (created_at, status);

CREATE INDEX IF NOT EXISTS idx_integration_logs_created_at_tenant
    ON integration_logs (created_at, tenant_id);
