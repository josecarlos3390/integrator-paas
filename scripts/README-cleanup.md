# Limpieza del Integrador para Circuitos de Prueba

## Cambio reciente: Naming Convention snake_case

Ahora EF Core usa `EFCore.NamingConventions` para generar **todas las tablas y columnas en snake_case** automáticamente. Esto evita el desorden de tener tablas en snake_case pero columnas en PascalCase.

---

## Pasos para limpiar todo y empezar desde cero

### 0. Aplicar la nueva migration (IMPORTANTE)

Primero hay que aplicar la migration que renombra todas las columnas a snake_case:

```powershell
dotnet ef database update --project src\Integration.Shared\Integration.Shared.csproj --startup-project src\Integration.Api\Integration.Api.csproj
```

> Si la base está vacía o tenés problemas, podés dropearla y recrearla:
> ```powershell
> docker exec integration-postgres dropdb -U integration integration_bus
> docker exec integration-postgres createdb -U integration integration_bus
> dotnet ef database update --project src\Integration.Shared\Integration.Shared.csproj --startup-project src\Integration.Api\Integration.Api.csproj
> ```

### 1. Parar API y Worker

```powershell
# En las consolas donde corren:
Ctrl + C
```

### 2. Limpiar PostgreSQL

```powershell
.\scripts\cleanup-all.ps1
```

### 3. Insertar tenant por defecto

```powershell
.\scripts\seed-tenant.ps1
```

### 4. Limpiar HANA (manual vía HANA Studio / DBeaver)

```sql
TRUNCATE TABLE INTEGRATION_BUS.OUTBOX_EVENTS;
```

Verificar que quedó vacía:
```sql
SELECT COUNT(*) FROM INTEGRATION_BUS.OUTBOX_EVENTS;
```

### 5. Recrear el SP en HANA

Ejecutar en HANA Studio / DBeaver:
```sql
-- scripts/hana-post-transaction-notice-v2.sql
```

### 6. Levantar de nuevo

```powershell
docker-compose up -d

dotnet run --project src\Integration.Api
dotnet run --project src\Integration.Worker
```

### 7. Hacer un circuito de prueba

1. Crear un BP en SAP (o cualquier documento autorizado: `2`, `4`, `13`, `17`)
2. Verificar que aparece en HANA:
   ```sql
   SELECT * FROM INTEGRATION_BUS.OUTBOX_EVENTS;
   ```
3. Verificar en el dashboard que:
   - **Doc** muestra `BP`, `Item`, `Inv` u `Ord`
   - **Op** muestra `Created`, `Updated` o `Deleted`
   - **AggregateId** tiene el `CardCode`, `ItemCode` o `DocEntry`

---

## Scripts disponibles

| Script | Uso |
|---|---|
| `cleanup-all.ps1` | Limpia todas las tablas PostgreSQL |
| `seed-tenant.ps1` | Inserta `tenant-001` |
| `update-tenant.ps1` | Actualiza campos del tenant |
| `cleanup-hana.sql` | Trunca `OUTBOX_EVENTS` en HANA |
| `hana-post-transaction-notice-v2.sql` | Recrea el SP en HANA |
