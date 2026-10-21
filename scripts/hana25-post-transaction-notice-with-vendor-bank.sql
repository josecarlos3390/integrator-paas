-- ============================================================================
-- RETAIL_QA5 / SBO_SP_PostTransactionNotice
-- Full outbox SP: clientes, articulos, precios especiales, facturas y pedidos
-- + VENDOR_BANK_ALERT (cambio de cuenta bancaria de proveedor -> Telegram).
--
-- TENANT_ID = nombre del schema/company DB de origen: RETAIL_QA5.
-- Desplegar en el schema RETAIL_QA5. Hacer backup del procedure actual antes
-- de reemplazarlo.
-- ============================================================================

CREATE OR REPLACE PROCEDURE SBO_SP_PostTransactionNotice
(
	in object_type nvarchar(30), 				-- SBO Object Type
	in transaction_type nchar(1),			-- [A]dd, [U]pdate, [D]elete, [C]ancel, C[L]ose
	in num_of_cols_in_key int,
	in list_of_key_cols_tab_del nvarchar(255),
	in list_of_cols_val_tab_del nvarchar(255)
)
LANGUAGE SQLSCRIPT
AS


-- Return values
error  int;				-- Result (0 for no error)
error_message nvarchar (200); 		-- Error string to be displayed
begin

DECLARE v_event_type NVARCHAR(128);
DECLARE v_aggregate_id NVARCHAR(256);
DECLARE v_tenant_id NVARCHAR(64) := 'RETAIL_QA5';
DECLARE v_card_type NVARCHAR(10);
DECLARE v_user_sign INT;

DECLARE v_payload       NCLOB;
DECLARE v_key_columns   NVARCHAR(255);
DECLARE v_key_values    NVARCHAR(500);


error := 0;
error_message := N'Ok';

--------------------------------------------------------------------------------------------------------------------------------

--	ADD	YOUR	CODE	HERE

---------------- EMPLEADOS (REP.LEGALES - SUST. CONTROLADAS)  -------------------------

IF object_type = '171' AND (transaction_type = ('U') OR transaction_type = ('A'))
THEN
	call BAG_REPRES_LEGALES(list_of_cols_val_tab_del,transaction_type,object_type,error, error_message);
END IF;

IF object_type = '7' AND (transaction_type = ('U') OR transaction_type = ('A'))
THEN
	INSERT INTO "AUDIT_RETAIL"."POSTTRANSACTION_LOG"
(
    "ObjectType",
    "TransactionType",
    "NumOfColsInKey",
    "ListOfKeyColsTabDel",
    "ListOfColsValTabDel"
)
VALUES
(
    :object_type,
    :transaction_type,
    :num_of_cols_in_key,
    :list_of_key_cols_tab_del,
    :list_of_cols_val_tab_del
);
END IF;

-- =============================================================================
-- SBO_SP_PostTransactionNotice: eventos para integration-paas / chatbot
--
-- 1. En el bloque DECLARE inicial del procedure, agregar:
--
--    DECLARE v_payload       NCLOB;
--    DECLARE v_key_columns   NVARCHAR(255);
--    DECLARE v_key_values    NVARCHAR(500);
--
-- 2. Cambiar el tenant existente:
--
--    v_tenant_id NVARCHAR(64) := 'RETAIL_QA5';
--
-- 3. Pegar los bloques de abajo después del bloque de proveedores y antes
--    de la llamada a BAG_COLA_SYNC_A_ZOHO_Y_WOO.
-- =============================================================================

-- -----------------------------------------------------------------------------
-- Clientes (Business Partner, CardType = C)
-- -----------------------------------------------------------------------------
IF object_type = '2'
   AND transaction_type IN ('A', 'U', 'D')
THEN
    v_aggregate_id := :list_of_cols_val_tab_del;

    SELECT "CardType"
      INTO v_card_type
      FROM "OCRD"
     WHERE "CardCode" = :v_aggregate_id;

    IF :v_card_type = 'C' THEN

        v_event_type :=
            CASE :transaction_type
                WHEN 'A' THEN 'Created'
                WHEN 'U' THEN 'Updated'
                WHEN 'D' THEN 'Deleted'
            END;

        INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS
        (
            ID, TENANT_ID, EVENT_TYPE, OBJECT_TYPE, AGGREGATE_ID, PAYLOAD,
            OCCURRED_AT, PROCESSED_AT, ATTEMPT_COUNT, ERROR_MESSAGE,
            IS_DEAD_LETTER
        )
        VALUES
        (
            SYSUUID,
            :v_tenant_id,
            :v_event_type,
            '2',
            :v_aggregate_id,
            NULL,
            CURRENT_TIMESTAMP, NULL, 0, NULL, 0
        );

    END IF;
END IF;

-- -----------------------------------------------------------------------------
-- Proveedores (Business Partner, CardType = S): alerta anti-fraude bancaria
-- Encola OBJECT_TYPE='VENDOR_BANK_ALERT'; el Worker compara contra el snapshot
-- y notifica por Telegram solo si cambia la cuenta bancaria.
-- -----------------------------------------------------------------------------
IF object_type = '2'
   AND transaction_type IN ('A', 'U')
THEN
    v_aggregate_id := :list_of_cols_val_tab_del;

    SELECT "CardType", "UserSign2"
      INTO v_card_type, v_user_sign
      FROM "OCRD"
     WHERE "CardCode" = :v_aggregate_id;

    IF :v_card_type = 'S' THEN

        v_event_type :=
            CASE :transaction_type
                WHEN 'A' THEN 'Created'
                WHEN 'U' THEN 'Updated'
            END;

        INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS
        (
            ID, TENANT_ID, EVENT_TYPE, OBJECT_TYPE, AGGREGATE_ID, PAYLOAD,
            OCCURRED_AT, PROCESSED_AT, ATTEMPT_COUNT, ERROR_MESSAGE,
            IS_DEAD_LETTER
        )
        VALUES
        (
            SYSUUID,
            :v_tenant_id,
            :v_event_type,
            'VENDOR_BANK_ALERT',
            :v_aggregate_id,
            '{"userSign": ' || COALESCE(TO_NVARCHAR(:v_user_sign), 'null') || '}',
            CURRENT_TIMESTAMP, NULL, 0, NULL, 0
        );

    END IF;
END IF;


-- -----------------------------------------------------------------------------
-- Artículos (ItemCode)
-- -----------------------------------------------------------------------------
IF object_type = '4'
   AND transaction_type IN ('A', 'U', 'D')
THEN
    v_event_type :=
        CASE :transaction_type
            WHEN 'A' THEN 'Created'
            WHEN 'U' THEN 'Updated'
            WHEN 'D' THEN 'Deleted'
        END;

    INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS
    (
        ID, TENANT_ID, EVENT_TYPE, OBJECT_TYPE, AGGREGATE_ID, PAYLOAD,
        OCCURRED_AT, PROCESSED_AT, ATTEMPT_COUNT, ERROR_MESSAGE,
        IS_DEAD_LETTER
    )
    VALUES
    (
        SYSUUID,
        :v_tenant_id,
        :v_event_type,
        '4',
        :list_of_cols_val_tab_del,
        NULL,
        CURRENT_TIMESTAMP, NULL, 0, NULL, 0
    );
END IF;

-- -----------------------------------------------------------------------------
-- Special Prices (CardCode + ItemCode)
--
-- El evento guarda las claves en PAYLOAD; el worker del PaaS consulta el
-- detalle real en el Service Layer y publica un evento normalizado.
-- -----------------------------------------------------------------------------
IF object_type = '7'
   AND transaction_type IN ('A', 'U', 'D')
THEN
    v_event_type :=
        CASE :transaction_type
            WHEN 'A' THEN 'Created'
            WHEN 'U' THEN 'Updated'
            WHEN 'D' THEN 'Deleted'
        END;

    v_key_columns := :list_of_key_cols_tab_del;
    v_key_values  := :list_of_cols_val_tab_del;

    v_payload :=
        '{"keyColumns":"' ||
        REPLACE(:v_key_columns, '"', '\"') ||
        '","keyValues":"' ||
        REPLACE(:v_key_values, '"', '\"') ||
        '"}';

    INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS
    (
        ID, TENANT_ID, EVENT_TYPE, OBJECT_TYPE, AGGREGATE_ID, PAYLOAD,
        OCCURRED_AT, PROCESSED_AT, ATTEMPT_COUNT, ERROR_MESSAGE,
        IS_DEAD_LETTER
    )
    VALUES
    (
        SYSUUID,
        :v_tenant_id,
        :v_event_type,
        '7',
        :v_key_values,
        :v_payload,
        CURRENT_TIMESTAMP, NULL, 0, NULL, 0
    );
END IF;

-- -----------------------------------------------------------------------------
-- Facturas y pedidos (DocEntry)
-- -----------------------------------------------------------------------------
IF object_type IN ('13', '17')
   AND transaction_type IN ('A', 'U', 'D')
THEN
    v_event_type :=
        CASE :transaction_type
            WHEN 'A' THEN 'Created'
            WHEN 'U' THEN 'Updated'
            WHEN 'D' THEN 'Deleted'
        END;

    INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS
    (
        ID, TENANT_ID, EVENT_TYPE, OBJECT_TYPE, AGGREGATE_ID, PAYLOAD,
        OCCURRED_AT, PROCESSED_AT, ATTEMPT_COUNT, ERROR_MESSAGE,
        IS_DEAD_LETTER
    )
    VALUES
    (
        SYSUUID,
        :v_tenant_id,
        :v_event_type,
        :object_type,
        :list_of_cols_val_tab_del,
        NULL,
        CURRENT_TIMESTAMP, NULL, 0, NULL, 0
    );
END IF;

----------------  -------------------------
/*
	Items = 4  - PK = ItemCode
	Sales Order = 17 - PK = DocEntry
	Inventory Transfer = 67 - PK= DocEntry
	Goods Receipt PO = 20 - PK= DocEntry
	Special Prices = 7 - PK = CardCode, ItemCode
	Business Partner = 2 - PK = CardCode
	Inventory Posting = 10000071 - PK = DocEntry
	Inventory Revaluation = 162 - PK = DocEntry
	Inventory Opening Balance = 310000001 - PK = DocEntry
	Inventory Transfer Request = 1250000001 - PK = DocEntry
	G/L Account Determination Criteria – Inventory = 1470000048 - PK = DmcId
	Inventory Counting = 1470000065 - PK = DocEntry
	Production Order = 202 - PK = DocEntry
	*Purchase Order = 22 - PK = DocEntry
	Goods Receipt = 59 - PK = DocEntry
	
*/
	   
IF IFNULL(error,0) = 0 
THEN

	
	
------------------- ZOHO ------------------------------	
	
	IF (
		    ( object_type = '2' 		AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') ) --Business Partner
		 or ( object_type = '4'  		AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--Items
		 or ( object_type = '7' 		AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--Special Prices
		 or ( object_type = '13' 		AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--A/R Invoice oinv
		 or ( object_type = '15' 		AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--Entrega
		 or ( object_type = '17' 		AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--Sales Order		
		 or ( object_type = '20' 		AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--entrada
		 or ( object_type = '22' 		AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--Pedido - Orden de compra
		 or ( object_type = '59'		AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--entrada de inventario
		 or ( object_type = '60'		AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--salida de inventario
		 or ( object_type = '67'		AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') ) --transferencia
		 or ( object_type = '162'		AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--Inventory Revaluation
		 or ( object_type = '202'		AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--Production Order
		 or ( object_type = '10000071' 	AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--Inventory Posting
		 or ( object_type = '310000001' AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--Inventory Opening Balance
		 or ( object_type = '1250000001'AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--Inventory Transfer Request
		 or ( object_type = '1470000048'AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--G/L Account Determination Criteria
		 or ( object_type = '1470000065'AND (transaction_type = 'A' OR transaction_type = 'U' OR  transaction_type = 'D') )	--Inventory Counting 
	   )
	THEN
		-- REGISTRA EVENTOS EN LA TABLA @SYNC_A_ZOHO_Y_WOO cuando se crea o modifica dentro de los formularios validados en el IF
		call BAG_COLA_SYNC_A_ZOHO_Y_WOO(list_of_cols_val_tab_del,transaction_type,object_type,error, error_message);
	END IF;
END IF;



--------------------------------------------------------------------------------------------------------------------------------
-- Select the return values
select :error, :error_message FROM dummy;

end;
