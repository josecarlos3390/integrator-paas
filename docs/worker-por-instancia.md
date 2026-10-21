# Worker por instancia SAP

## Decisión

**Un Worker por schema/company DB.** El `TENANT_ID` del outbox es el nombre del
schema de origen, no un identificador genérico. `tenant-001` quedó retirado.

```text
Worker VIAGGIO_QA  → HANA hanaroda   → Service Layer VIAGGIO_QA
Worker RETAIL_QA5 → HANA hanaroda25 → Service Layer RETAIL_QA5
```

El código es el mismo; cambia la configuración de cada proceso.

## Variables por Worker

### RETAIL_QA5 (hanaroda25)

```env
Tenants__DefaultTenantId=RETAIL_QA5
Hana__ConnectionString=Driver={HDBODBC};SERVERNODE=hanaroda25.gruporoda.com:30015;Database=RETAIL_QA5;UID=...;PWD=...;CS=INTEGRATION_BUS;
Sap__ServiceLayerUrl=https://hanaroda25.gruporoda.com:50000
Sap__CompanyDB=RETAIL_QA5
```

### VIAGGIO_QA (hanaroda)

```env
Tenants__DefaultTenantId=VIAGGIO_QA
Hana__ConnectionString=Driver={HDBODBC};SERVERNODE=hanaroda.gruporoda.com:30015;Database=INTEGRATION_BUS;UID=...;PWD=...;CS=INTEGRATION_BUS;
Sap__ServiceLayerUrl=https://hanaroda.gruporoda.com:50000
Sap__CompanyDB=VIAGGIO_QA
```

## Script

```powershell
# RETAIL_QA5
.\scripts\start-worker.ps1 `
  -TenantId RETAIL_QA5 `
  -HanaConnectionString "Driver={HDBODBC};SERVERNODE=hanaroda25.gruporoda.com:30015;Database=RETAIL_QA5;UID=...;PWD=...;CS=INTEGRATION_BUS;" `
  -SapServiceLayerUrl "https://hanaroda25.gruporoda.com:50000" `
  -SapCompanyDb "RETAIL_QA5"

# VIAGGIO_QA
.\scripts\start-worker.ps1 `
  -TenantId VIAGGIO_QA `
  -HanaConnectionString "Driver={HDBODBC};SERVERNODE=hanaroda.gruporoda.com:30015;Database=INTEGRATION_BUS;UID=...;PWD=...;CS=INTEGRATION_BUS;" `
  -SapServiceLayerUrl "https://hanaroda.gruporoda.com:50000" `
  -SapCompanyDb "VIAGGIO_QA"
```

## Requisitos HANA

El usuario del Worker necesita:

```sql
GRANT SELECT, UPDATE ON INTEGRATION_BUS.OUTBOX_EVENTS TO CHATBOT_INTEGRATION;
GRANT SELECT ON SCHEMA INTEGRATION_BUS TO CHATBOT_INTEGRATION;
```

No usar `SYSTEM` en producción.

## Tenant

Debe existir en `tenant_config` con el mismo nombre del schema:

```text
tenant_id          RETAIL_QA5
sap_company_db     RETAIL_QA5
sap_service_layer  https://hanaroda25.gruporoda.com:50000
```

Y el SP del schema debe escribir el mismo tenant:

```sql
-- RETAIL_QA5
v_tenant_id := 'RETAIL_QA5';

-- VIAGGIO_QA
v_tenant_id := 'VIAGGIO_QA';
```

Para RETAIL_QA5 el SP completo (genérico + `VENDOR_BANK_ALERT`) está en
`scripts/hana25-post-transaction-notice-with-vendor-bank.sql`.
El script `hana25-post-transaction-vendor-bank-alert.sql` reemplaza todo el
procedure, así que solo debe usarse si el schema no tiene el SP genérico.

El bot de WhatsApp:

```env
SAP_EVENT_TENANT=RETAIL_QA5
```

Su cola `chatbot.sap.events` debe estar bindeada a:

```text
RETAIL_QA5.4.*
RETAIL_QA5.7.*
RETAIL_QA5.13.*
```

## Verificación

```sql
SELECT ID, TENANT_ID, OBJECT_TYPE, EVENT_TYPE, AGGREGATE_ID, PROCESSED_AT
FROM INTEGRATION_BUS.OUTBOX_EVENTS
WHERE ID = 'D41870808AE28C0B1A007800B7CAF3A0';
```

Luego:

```sql
SELECT event_id, object_type, aggregate_id, processed_at, error
FROM sap_event_inbox
ORDER BY rowid DESC
LIMIT 10;
```
