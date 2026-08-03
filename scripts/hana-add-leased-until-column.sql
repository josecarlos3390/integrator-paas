-- ============================================================================
-- Migration: Add LEASED_UNTIL column to OUTBOX_EVENTS
-- Required for Fase 2 batch lease acquisition (SaaS scalability hardening)
-- ============================================================================

DO BEGIN
    DECLARE col_exists INT := 0;
    SELECT COUNT(*) INTO col_exists
    FROM TABLE_COLUMNS
    WHERE SCHEMA_NAME = 'INTEGRATION_BUS'
      AND TABLE_NAME = 'OUTBOX_EVENTS'
      AND COLUMN_NAME = 'LEASED_UNTIL';

    IF :col_exists = 0 THEN
        EXEC 'ALTER TABLE INTEGRATION_BUS.OUTBOX_EVENTS ADD (LEASED_UNTIL TIMESTAMP NULL)';
    END IF;
END;

-- Verify
SELECT COLUMN_NAME, DATA_TYPE_NAME, IS_NULLABLE
FROM TABLE_COLUMNS
WHERE SCHEMA_NAME = 'INTEGRATION_BUS'
  AND TABLE_NAME = 'OUTBOX_EVENTS'
  AND COLUMN_NAME = 'LEASED_UNTIL';
