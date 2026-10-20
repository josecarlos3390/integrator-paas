# Worker por instancia SAP

## Decisión

Para el piloto: **un proceso Worker por instancia SAP**.

```text
Worker viaggio-qa   → HANA hanaroda   → Service Layer VIAGGIO_QA
Worker retail-qa5   → HANA hanaroda25 → Service Layer RETAIL_QA5
```

El código es el mismo; cambia la configuración de cada proceso.

Más adelante se puede implementar un Worker con múltiples pollers, pero
requiere un pool HANA y un repositorio por tenant.

## Variables por Worker

```env
Tenants__DefaultTenantId=retail-qa5
Hana__ConnectionString=Driver={HDBODBC};SERVERNODE=hanaroda25.gruporoda.com:30015;Database=RETAIL_QA5;UID=...;PWD=...;CS=INTEGRATION_BUS;
Sap__ServiceLayerUrl=https://hanaroda25.gruporoda.com:50000
Sap__CompanyDB=RETAIL_QA5
```

## Script

```powershell
.\scripts\start-worker.ps1 `
  -TenantId retail-qa5 `
  -HanaConnectionString "Driver={HDBODBC};SERVERNODE=hanaroda25.gruporoda.com:30015;Database=RETAIL_QA5;UID=...;PWD=...;CS=INTEGRATION_BUS;" `
  -SapServiceLayerUrl "https://hanaroda25.gruporoda.com:50000" `
  -SapCompanyDb "RETAIL_QA5"
```

## Requisitos HANA

El usuario del Worker necesita:

```sql
GRANT SELECT, UPDATE ON INTEGRATION_BUS.OUTBOX_EVENTS TO CHATBOT_INTEGRATION;
GRANT SELECT ON SCHEMA INTEGRATION_BUS TO CHATBOT_INTEGRATION;
```

No usar `SYSTEM` en producción.

## Tenant

Debe existir en `tenant_config`:

```text
tenant_id          retail-qa5
sap_company_db     RETAIL_QA5
sap_service_layer  https://hanaroda25.gruporoda.com:50000
```

Y el SP debe escribir el mismo tenant:

```sql
v_tenant_id := 'retail-qa5';
```

El bot:

```env
SAP_EVENT_TENANT=retail-qa5
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
