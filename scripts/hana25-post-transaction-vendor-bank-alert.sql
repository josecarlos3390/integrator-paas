-- ============================================================================
-- SBO_SP_PostTransactionNotice — RETAIL (hanaroda25 / RETAIL_QA5)
-- Flujo: VENDOR_BANK_ALERT (alerta anti-fraude por cambio de cuenta bancaria)
--
-- Encola en INTEGRATION_BUS.OUTBOX_EVENTS unicamente:
--   - ObjectType '2' (BusinessPartners) con CardType = 'S' (proveedores)
--   - Operaciones Add (A) y Update (U)
--
-- OBJECT_TYPE = 'VENDOR_BANK_ALERT' para que el Worker lo procese con el
-- flujo de alerta (snapshot + Telegram) y no como sync de BP al CRM.
-- PAYLOAD lleva el UserSign2 (usuario SAP que hizo el cambio) como JSON.
--
-- Instalar en el schema de la compania (RETAIL_QA5) via hdbsql:
--   hdbsql -n hanaroda25.gruporoda.com:30015 -u B1ADMIN -p *** -I hana25-post-transaction-vendor-bank-alert.sql
-- ============================================================================

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
    DECLARE v_tenant_id NVARCHAR(64) := 'RETAIL';
    DECLARE v_card_type NVARCHAR(10);
    DECLARE v_user_sign INT;

    -- Nunca devolver error a SAP: un fallo del outbox no debe
    -- bloquear la transaccion del usuario.
    DECLARE EXIT HANDLER FOR SQLEXCEPTION
    BEGIN
        error := 0;
        error_message := N'Ok';
        SELECT :error, :error_message FROM DUMMY;
    END;

    ----------------------------------------------------------------
    -- Solo BusinessPartners (object_type = '2'), Add o Update
    ----------------------------------------------------------------
    IF :object_type <> '2' OR :transaction_type NOT IN ('A', 'U') THEN
        SELECT :error, :error_message FROM DUMMY;
        RETURN;
    END IF;

    v_aggregate_id := :list_of_cols_val_tab_del;

    ----------------------------------------------------------------
    -- Solo proveedores (CardType = 'S'); clientes se ignoran
    ----------------------------------------------------------------
    SELECT "CardType", "UserSign2"
      INTO v_card_type, v_user_sign
      FROM "OCRD"
     WHERE "CardCode" = :v_aggregate_id;

    IF :v_card_type <> 'S' THEN
        SELECT :error, :error_message FROM DUMMY;
        RETURN;
    END IF;

    ----------------------------------------------------------------
    -- Mapear operacion y encolar el evento de alerta
    ----------------------------------------------------------------
    IF :transaction_type = 'A' THEN
        v_event_type := 'Created';
    ELSE
        v_event_type := 'Updated';
    END IF;

    INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS
    (
        ID,
        TENANT_ID,
        EVENT_TYPE,
        OBJECT_TYPE,
        AGGREGATE_ID,
        PAYLOAD,
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
        'VENDOR_BANK_ALERT',
        :v_aggregate_id,
        '{"userSign": ' || COALESCE(TO_NVARCHAR(:v_user_sign), 'null') || '}',
        CURRENT_TIMESTAMP,
        NULL,
        0,
        NULL,
        0
    );

    SELECT :error, :error_message FROM DUMMY;

END;
