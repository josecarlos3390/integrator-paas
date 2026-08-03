-- ============================================================================
-- Agregar columna PAYLOAD a OUTBOX_EVENTS para soportar eventos de polling
-- ============================================================================

DO BEGIN
    DECLARE col_exists INT := 0;
    SELECT COUNT(*) INTO col_exists 
    FROM TABLE_COLUMNS 
    WHERE SCHEMA_NAME = 'INTEGRATION_BUS' 
      AND TABLE_NAME = 'OUTBOX_EVENTS' 
      AND COLUMN_NAME = 'PAYLOAD';
    
    IF :col_exists = 0 THEN
        EXEC 'ALTER TABLE INTEGRATION_BUS.OUTBOX_EVENTS ADD (PAYLOAD NCLOB)';
    END IF;
END;

SELECT 'Columns in OUTBOX_EVENTS:' AS msg FROM DUMMY;
SELECT COLUMN_NAME, DATA_TYPE_NAME 
FROM TABLE_COLUMNS 
WHERE SCHEMA_NAME = 'INTEGRATION_BUS' 
  AND TABLE_NAME = 'OUTBOX_EVENTS';
