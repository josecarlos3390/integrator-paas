-- ============================================================================
-- SBO_SP_PostTransactionNotice
-- Stored procedure que captura transacciones de SAP B1 y las inserta
-- en la tabla INTEGRATION_BUS.OUTBOX_EVENTS para procesamiento async.
-- 
-- IMPORTANTE:
-- - Este SP se ejecuta DENTRO de la transaccion de SAP.
-- - NUNCA debe lanzar una excepcion o retornar error != 0,
--   porque eso haria ROLLBACK de la transaccion del usuario.
-- - Usamos EXIT HANDLER FOR SQLEXCEPTION para silenciar cualquier
--   fallo del INSERT en el outbox.
-- ============================================================================

CREATE OR REPLACE PROCEDURE SBO_SP_PostTransactionNotice
(
    IN object_type NVARCHAR(30),            -- SBO Object Type
    IN transaction_type NCHAR(1),           -- [A]dd, [U]pdate, [D]elete, [C]ancel, C[L]ose
    IN num_of_cols_in_key INT,
    IN list_of_key_cols_tab_del NVARCHAR(255),
    IN list_of_cols_val_tab_del NVARCHAR(255)
)
LANGUAGE SQLSCRIPT
AS
BEGIN
    -- Resultados por defecto: siempre exito para SAP
    DECLARE error INT := 0;
    DECLARE error_message NVARCHAR(200) := N'Ok';
    
    -- Variables para el evento
    DECLARE v_event_type NVARCHAR(128);
    DECLARE v_card_code NVARCHAR(128);
    DECLARE v_tenant_id NVARCHAR(64) := 'tenant-001';

    --------------------------------------------------------------------------------------------------------------------------------
    -- BUSINESS PARTNERS (Object Type = '2')
    --------------------------------------------------------------------------------------------------------------------------------
    IF :object_type = '2' THEN
        
        -- Para BusinessPartners la key principal es CardCode.
        -- list_of_cols_val_tab_del contiene el CardCode directamente
        -- (num_of_cols_in_key = 1 para BP).
        v_card_code := :list_of_cols_val_tab_del;
        
        -- Mapear tipo de transaccion a evento de negocio
        IF :transaction_type = 'A' THEN
            v_event_type := 'CustomerCreated';
        ELSEIF :transaction_type = 'U' THEN
            v_event_type := 'CustomerUpdated';
        ELSEIF :transaction_type = 'D' THEN
            v_event_type := 'CustomerDeleted';
        ELSE
            -- Cancel, Close u otros: los tratamos como update
            v_event_type := 'CustomerUpdated';
        END IF;

        -- Insertar en outbox de forma SEGURA.
        -- Si el INSERT falla (tabla no existe, locks, etc.),
        -- el EXIT HANDLER captura el error y NO afecta la transaccion de SAP.
        DECLARE EXIT HANDLER FOR SQLEXCEPTION
        BEGIN
            -- Silenciosamente ignoramos cualquier error del INSERT.
            -- El error se loguea en SAP (tabla de sistema) pero no se propaga.
            error := 0;
            error_message := N'Ok';
        END;

        INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS 
        (
            ID, 
            TENANT_ID, 
            EVENT_TYPE, 
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
            :v_card_code, 
            CURRENT_TIMESTAMP, 
            NULL, 
            0, 
            NULL, 
            0
        );

    END IF;

    --------------------------------------------------------------------------------------------------------------------------------
    -- ARTICULOS / ITEMS (Object Type = '4') -- Template listo para descomentar
    --------------------------------------------------------------------------------------------------------------------------------
    -- IF :object_type = '4' THEN
    --     DECLARE v_item_code NVARCHAR(128);
    --     DECLARE v_item_event_type NVARCHAR(128);
    --     
    --     v_item_code := :list_of_cols_val_tab_del;
    --     
    --     IF :transaction_type = 'A' THEN
    --         v_item_event_type := 'ItemCreated';
    --     ELSEIF :transaction_type = 'U' THEN
    --         v_item_event_type := 'ItemUpdated';
    --     ELSEIF :transaction_type = 'D' THEN
    --         v_item_event_type := 'ItemDeleted';
    --     ELSE
    --         v_item_event_type := 'ItemUpdated';
    --     END IF;
    --     
    --     DECLARE EXIT HANDLER FOR SQLEXCEPTION
    --     BEGIN
    --         error := 0;
    --         error_message := N'Ok';
    --     END;
    --     
    --     INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS 
    --     (ID, TENANT_ID, EVENT_TYPE, AGGREGATE_ID, OCCURRED_AT, PROCESSED_AT, ATTEMPT_COUNT, ERROR_MESSAGE, IS_DEAD_LETTER)
    --     VALUES (SYSUUID(), :v_tenant_id, :v_item_event_type, :v_item_code, CURRENT_TIMESTAMP, NULL, 0, NULL, 0);
    -- END IF;

    --------------------------------------------------------------------------------------------------------------------------------
    -- DOCUMENTOS DE VENTA (Object Type = '17' = Orders, '13' = Invoices)
    -- Template listo para descomentar cuando se mapeen
    --------------------------------------------------------------------------------------------------------------------------------
    -- IF :object_type = '17' THEN
    --     ... SalesOrderCreated / SalesOrderUpdated ...
    -- END IF;
    --
    -- IF :object_type = '13' THEN
    --     ... InvoiceCreated / InvoiceUpdated ...
    -- END IF;

    --------------------------------------------------------------------------------------------------------------------------------
    -- Retornar valores a SAP (SIEMPRE exito)
    --------------------------------------------------------------------------------------------------------------------------------
    SELECT :error, :error_message FROM DUMMY;

END;
