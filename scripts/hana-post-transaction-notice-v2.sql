-- ============================================================================
-- SBO_SP_PostTransactionNotice v2
-- 
-- Estandarizacion de eventos:
--   EVENT_TYPE  = Created | Updated | Deleted  (siempre)
--   OBJECT_TYPE = '2' | '4' | '13' | '17' ... (tipo de documento SAP)
-- 
-- Esto evita la explosion combinatoria de event types.
-- Agregar un nuevo documento = 0 lineas de CASE, solo dejar pasar el object_type.
-- ============================================================================

-- 1) Agregar columna OBJECT_TYPE si no existe
DO BEGIN
    DECLARE col_exists INT := 0;
    SELECT COUNT(*) INTO col_exists 
    FROM TABLE_COLUMNS 
    WHERE SCHEMA_NAME = 'INTEGRATION_BUS' 
      AND TABLE_NAME = 'OUTBOX_EVENTS' 
      AND COLUMN_NAME = 'OBJECT_TYPE';
    
    IF :col_exists = 0 THEN
        EXEC 'ALTER TABLE INTEGRATION_BUS.OUTBOX_EVENTS ADD (OBJECT_TYPE NVARCHAR(30))';
    END IF;
END;

-- 2) Recrear el stored procedure simplificado
CREATE OR REPLACE PROCEDURE SBO_SP_PostTransactionNotice
(
    IN object_type NVARCHAR(30),
    IN transaction_type NCHAR(1),
    IN num_of_cols_in_key INT,
    IN list_of_key_cols_tab_del NVARCHAR(255),
    IN list_of_cols_val_tab_del NVARCHAR(255)
)
LANGUAGE SQLSCRIPT
AS
BEGIN
    DECLARE error INT := 0;
    DECLARE error_message NVARCHAR(200) := N'Ok';
    
    DECLARE v_event_type NVARCHAR(128);
    DECLARE v_aggregate_id NVARCHAR(256);
    DECLARE v_tenant_id NVARCHAR(64) := 'tenant-001';

    --------------------------------------------------------------------------------------------------------------------------------
    -- Mapear transaction_type a operacion estandar
    --------------------------------------------------------------------------------------------------------------------------------
    IF :transaction_type = 'A' THEN
        v_event_type := 'Created';
    ELSEIF :transaction_type = 'U' THEN
        v_event_type := 'Updated';
    ELSEIF :transaction_type = 'D' THEN
        v_event_type := 'Deleted';
    ELSE
        -- Cancel, Close, etc. se tratan como Updated
        v_event_type := 'Updated';
    END IF;

    --------------------------------------------------------------------------------------------------------------------------------
    -- Extraer el AggregateId (la key del documento)
    -- Para la mayoria de documentos SAP es un solo valor en list_of_cols_val_tab_del
    --------------------------------------------------------------------------------------------------------------------------------
    v_aggregate_id := :list_of_cols_val_tab_del;

    --------------------------------------------------------------------------------------------------------------------------------
    -- Insertar en outbox solo para los object_types que nos interesan
    -- Descomentar/modificar segun los documentos que se vayan integrando
    --------------------------------------------------------------------------------------------------------------------------------
    IF :object_type IN ('2', '4', '13', '17', '15', '22', '14') THEN

        -- Loop prevention: skip documents that originated from the CRM
        IF :object_type = '17' THEN
            DECLARE v_sync_origin NVARCHAR(20);
            SELECT "U_SyncOrigin" INTO v_sync_origin FROM "ORDR" WHERE "DocEntry" = :v_aggregate_id;
            IF :v_sync_origin = 'CRM' THEN
                SELECT :error, :error_message FROM DUMMY;
                RETURN;
            END IF;
        END IF;

        DECLARE EXIT HANDLER FOR SQLEXCEPTION
        BEGIN
            error := 0;
            error_message := N'Ok';
        END;

        INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS 
        (
            ID, 
            TENANT_ID, 
            EVENT_TYPE, 
            OBJECT_TYPE,
            AGGREGATE_ID, 
            OCCURRED_AT, 
            PROCESSED_AT, 
            ATTEMPT_COUNT, 
            ERROR_MESSAGE, 
            IS_DEAD_LETTER
        )
        VALUES 
        (
            SYSUUID(), 
            :v_tenant_id, 
            :v_event_type, 
            :object_type,
            :v_aggregate_id, 
            CURRENT_TIMESTAMP, 
            NULL, 
            0, 
            NULL, 
            0
        );

    END IF;

    SELECT :error, :error_message FROM DUMMY;

END;
