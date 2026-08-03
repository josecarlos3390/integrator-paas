-- ============================================================================-- Limpieza completa de PostgreSQL para circuitos de prueba limpios-- ============================================================================
-- NOTA: tenant_config NO se trunca porque contiene configuración base de tenants.
TRUNCATE TABLE outbox_events RESTART IDENTITY CASCADE;
TRUNCATE TABLE integration_logs RESTART IDENTITY CASCADE;
TRUNCATE TABLE dead_letter_events RESTART IDENTITY CASCADE;
TRUNCATE TABLE idempotency_records RESTART IDENTITY CASCADE;
TRUNCATE TABLE integration_alerts RESTART IDENTITY CASCADE;
TRUNCATE TABLE processed_messages RESTART IDENTITY CASCADE;
TRUNCATE TABLE tenant_feature_flags RESTART IDENTITY CASCADE;
TRUNCATE TABLE polling_cursors RESTART IDENTITY CASCADE;
TRUNCATE TABLE price_snapshots RESTART IDENTITY CASCADE;

-- Verificar que quedaron vacías (excepto tenant_config)
SELECT 'outbox_events' AS tabla, COUNT(*) AS registros FROM outbox_events
UNION ALL
SELECT 'integration_logs', COUNT(*) FROM integration_logs
UNION ALL
SELECT 'dead_letter_events', COUNT(*) FROM dead_letter_events
UNION ALL
SELECT 'idempotency_records', COUNT(*) FROM idempotency_records
UNION ALL
SELECT 'integration_alerts', COUNT(*) FROM integration_alerts
UNION ALL
SELECT 'processed_messages', COUNT(*) FROM processed_messages
UNION ALL
SELECT 'tenant_feature_flags', COUNT(*) FROM tenant_feature_flags
UNION ALL
SELECT 'polling_cursors', COUNT(*) FROM polling_cursors
UNION ALL
SELECT 'price_snapshots', COUNT(*) FROM price_snapshots;
