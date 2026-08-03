-- ============================================================================
-- Script de prueba para INTEGRATION_BUS.OUTBOX_EVENTS
-- ============================================================================
-- Ejecuta esto directamente en SAP HANA (por ejemplo, desde la Consola SQL
-- de SAP HANA Studio o DBeaver) para simular que el Add-on creó facturas.
-- Luego verifica que el Integration.Worker las procese.
-- ============================================================================

INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS
    (ID, TENANT_ID, EVENT_TYPE, AGGREGATE_ID, OCCURRED_AT, PROCESSED_AT, ATTEMPT_COUNT, ERROR_MESSAGE, IS_DEAD_LETTER)
VALUES
    (SYSUUID, 'tenant-001', 'InvoiceCreated', '10001', CURRENT_TIMESTAMP, NULL, 0, NULL, 0);

INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS
    (ID, TENANT_ID, EVENT_TYPE, AGGREGATE_ID, OCCURRED_AT, PROCESSED_AT, ATTEMPT_COUNT, ERROR_MESSAGE, IS_DEAD_LETTER)
VALUES
    (SYSUUID, 'tenant-001', 'InvoiceCreated', '10002', CURRENT_TIMESTAMP, NULL, 0, NULL, 0);

INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS
    (ID, TENANT_ID, EVENT_TYPE, AGGREGATE_ID, OCCURRED_AT, PROCESSED_AT, ATTEMPT_COUNT, ERROR_MESSAGE, IS_DEAD_LETTER)
VALUES
    (SYSUUID, 'tenant-001', 'BusinessPartnerCreated', 'C20001', CURRENT_TIMESTAMP, NULL, 0, NULL, 0);

-- Verificar eventos pendientes
SELECT * FROM INTEGRATION_BUS.OUTBOX_EVENTS WHERE PROCESSED_AT IS NULL AND IS_DEAD_LETTER = 0;

-- Verificar eventos procesados
SELECT * FROM INTEGRATION_BUS.OUTBOX_EVENTS WHERE PROCESSED_AT IS NOT NULL;

-- Verificar dead letters
SELECT * FROM INTEGRATION_BUS.OUTBOX_EVENTS WHERE IS_DEAD_LETTER = 1;
