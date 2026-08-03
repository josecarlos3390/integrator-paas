-- ============================================================================
-- Schema INTEGRATION_BUS en SAP HANA
-- ============================================================================
-- Este schema reside en la instancia de SAP HANA del cliente.
-- El SAP Add-on (PostTransaction) escribe aquí; el OutboxDispatcherWorker lee.
-- ============================================================================

CREATE SCHEMA IF NOT EXISTS INTEGRATION_BUS;

-- ----------------------------------------------------------------------------
-- Tabla: OUTBOX_EVENTS
-- Buffer de staging para eventos generados por SAP B1.
-- ----------------------------------------------------------------------------
CREATE COLUMN TABLE IF NOT EXISTS INTEGRATION_BUS.OUTBOX_EVENTS (
    ID            NVARCHAR(36) PRIMARY KEY,   -- SYSUUID()
    TENANT_ID     NVARCHAR(64) NOT NULL,
    EVENT_TYPE    NVARCHAR(128) NOT NULL,
    AGGREGATE_ID  NVARCHAR(128) NOT NULL,     -- DocEntry, CardCode, etc.
    OCCURRED_AT   TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PROCESSED_AT  TIMESTAMP,
    ATTEMPT_COUNT INTEGER NOT NULL DEFAULT 0,
    ERROR_MESSAGE NVARCHAR(4000),
    IS_DEAD_LETTER TINYINT NOT NULL DEFAULT 0  -- 0 = activo, 1 = dead letter
);

-- Índices para lectura eficiente del dispatcher
-- NOTA: SAP HANA no soporta CREATE INDEX ... WHERE (filtered indexes).
-- En HANA Column Store el motor optimiza bien las consultas con predicados
-- sobre columnas individuales, pero un índice compuesto ayuda al ORDER BY.
CREATE INDEX IF NOT EXISTS IDX_OUTBOX_UNPROCESSED
    ON INTEGRATION_BUS.OUTBOX_EVENTS (IS_DEAD_LETTER, OCCURRED_AT, ATTEMPT_COUNT);

CREATE INDEX IF NOT EXISTS IDX_OUTBOX_TENANT_AGGREGATE
    ON INTEGRATION_BUS.OUTBOX_EVENTS (TENANT_ID, AGGREGATE_ID);

-- ----------------------------------------------------------------------------
-- Comentarios de documentación
-- ----------------------------------------------------------------------------
COMMENT ON TABLE INTEGRATION_BUS.OUTBOX_EVENTS IS 'Staging buffer de eventos de integración SAP→CRM. Escrita por SAP Add-on, leída por Integration.Worker.';
COMMENT ON COLUMN INTEGRATION_BUS.OUTBOX_EVENTS.ID IS 'UUID v4 generado con SYSUUID()';
COMMENT ON COLUMN INTEGRATION_BUS.OUTBOX_EVENTS.EVENT_TYPE IS 'InvoiceCreated, BusinessPartnerCreated, SalesOrderCreated, GoodsReceiptPO';
COMMENT ON COLUMN INTEGRATION_BUS.OUTBOX_EVENTS.AGGREGATE_ID IS 'Identificador del documento SAP (ej. DocEntry)';
COMMENT ON COLUMN INTEGRATION_BUS.OUTBOX_EVENTS.IS_DEAD_LETTER IS '1 si excedió los reintentos máximos';
