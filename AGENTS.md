# AGENTS.md — Integration Bus

Este archivo contiene contexto específico para agents que trabajen en este codebase.

## Convenciones de código

- **Idioma**: Código y comentarios en inglés. Documentación de usuario en español.
- **Namespaces**: `Integration.{Proyecto}.{Carpeta}`
- **Estilo**: C# moderno (records, pattern matching, `var` donde el tipo es obvio)
- **Async**: Todos los métodos I/O son async con sufijo `Async`. Usar `CancellationToken` siempre.
- **Nullables**: Habilitados en todos los proyectos (`<Nullable>enable</Nullable>`)

## Decisiones arquitectónicas importantes

### HANA ODBC
- No usar `CASE` en SQL parametrizado — falla con ODBC HANA. Usar queries separados o conditional aggregates con `COUNT` / `NULLIF`.
- No usar `SYSUUID()` en INSERTs parametrizados — generar UUID en C#.
- Usar Dapper para queries; EF solo para PostgreSQL.

### Multi-HANA
- El outbox polling soporta varios servidores HANA: `Hana.ConnectionString` (default) + `Hana.Connections` (nombrados). `HanaConnectionPoolRegistry` mantiene un pool por servidor.
- `HanaOutboxDispatcher` recibe su `HanaOutboxRepository` por `ActivatorUtilities` (nunca desde DI scoped, que inyecta el repo del servidor default). Al agregar procesamiento por evento, mantener ese patrón.
- El dashboard (`DashboardController`) también consulta TODOS los servidores del registry para `events` y `stats` (merge en memoria, filtro opcional `?tenantId=`); un servidor caído solo loguea warning y contribuye 0/vacío. El repo por servidor se crea con `ActivatorUtilities` igual que en el dispatcher.
- `LEASED_UNTIL` se escribe en UTC (`DateTime.UtcNow`) pero HANA lo compara con `CURRENT_TIMESTAMP` (hora local del servidor HANA). En servidores HANA fuera de UTC hay un desfase en la expiración del lease — conocido, no afecta el flujo normal.

### Serialización SAP
- SAP Service Layer requiere **PascalCase** exacto.
- Usar `PropertyNamingPolicy = null` en `JsonSerializerOptions`.

### Refit
- Rutas deben ser **absolutas** con leading `/`.
- `BaseUrl` debe ser el host root (`http://localhost:5000`), no incluir path.
- Refit 7.2.22 (actualizado desde 7.0.0 por GHSA-3hxg-fxwm-8gf7).

### Circuit Breaker
- **Siempre separar** pipelines de Polly por sistema destino (SAP vs CRM).
- Nunca compartir un circuit breaker entre tenants o entre sistemas.

### Tenant Routing
- `TenantClientFactory` es singleton que cachea `ServiceLayerClient` e `ICrmApiClient` por tenant usando `Lazy<Task<T>>` (AsyncLazy pattern). Todos los métodos son async.
- Cada tenant tiene su propio `CookieContainer` para SAP.
- Fallback a config global (`appsettings.json`) si el tenant no tiene valores propios.

### Idempotencia
- `IIdempotencyService.TryProcessAsync` envuelve TODO el procesamiento.
- Business errors NO se guardan (permiten retry manual).
- Transient errors SÍ se guardan (evitan spam de retries).
- TTL 30 días por defecto; cleanup diario vía `IdempotencyCleanupWorker`.

### Feature Flags
- Opt-out por defecto: si no existe registro, retorna `true`.
- Cache en memoria 30s para no saturar PostgreSQL.

### Alertas
- Deduplicación por tipo+tenant en ventana de 30 minutos.
- Webhook fire-and-forget (nunca bloquea el procesamiento).

### VENDOR_BANK_ALERT (alerta anti-fraude)
- `OBJECT_TYPE='VENDOR_BANK_ALERT'` en el outbox HANA = flujo de alerta, **no** sync al CRM. El SP solo encola proveedores (`CardType='S'`) y manda `UserSign2` en el `PAYLOAD` (`{"userSign": N}`).
- Detección por snapshot propio (`vendor_bank_snapshots`): sin baseline se aprende en silencio, nunca se alerta en la primera vista.
- La firma compara la **colección completa OCRB** (`accounts_signature`: filas `banco|sucursal|cuenta|iban` normalizadas y ordenadas), no solo la cuenta por defecto del encabezado — detecta cuentas agregadas/eliminadas/modificadas.
- VENDOR_BANK_ALERT **bypassea la guarda de idempotencia** en `HanaOutboxDispatcher`: la idempotencia llavea por tenant+objectType+aggregateId y descartaría todos los updates posteriores al primero del mismo proveedor. Es comparación de estado, no procesamiento de documento.
- Telegram (`TelegramNotifier`) es fire-and-forget: nunca lanza excepción; un fallo solo se loguea.
- Baseline inicial: `POST /api/admin/vendor-bank/baseline?tenantId=X`. Las rutas `/api/admin/**` NO pasan por `ApiKeyMiddleware` — el tenant siempre va explícito por query param.

## Health Checks

- `/health/live` — proceso vivo (sin verificación de dependencias)
- `/health/ready` — PostgreSQL + SAP Service Layer accesibles
- Usar para readiness probes en Kubernetes/Docker Swarm.

## Graceful Shutdown

- `OutboxDispatcherWorker` espera hasta 30s antes de forzar cancelación del ciclo actual.
- No se inician nuevos ciclos después de recibir señal de stop.

## Windows Services (SRVGLPI01)

- API y Worker corren como servicios de Windows (`Integration.Api`, `Integration.Worker`), inicio automático + restart-on-failure. Binarios publicados en `.publish\Api` y `.publish\Worker`.
- Instalar/reinstalar: `scripts\install-windows-services.ps1` (elevado; `-Uninstall` para quitar).
- Tras un pull con cambios: `dotnet publish -c Release -o .publish\{Api,Worker}` + `Restart-Service`. Los servicios corren los binarios de `.publish\`, NO `dotnet run` desde `src\`.
- Los proyectos usan `AddWindowsService()` (Microsoft.Extensions.Hosting.WindowsServices) — no quitar esa llamada.

## Docker

- No ejecutar `git commit`, `git push` o mutaciones de git sin confirmación explícita del usuario.
- No instalar paquetes globales fuera del directorio de trabajo.
- Puerto de PostgreSQL: **5434** (no 5432) para evitar conflictos.

## Test endpoints útiles

```bash
# Simular eventos
curl -X POST "http://localhost:5000/api/test/simulate-invoice?docEntry=1234"
curl -X POST "http://localhost:5000/api/test/simulate-customer?cardCode=C000004&eventType=CustomerCreated"

# Health
curl http://localhost:5000/health/live
curl http://localhost:5000/health/ready

# Stats
curl http://localhost:5000/api/dashboard/stats
curl http://localhost:5000/api/admin/alerts/stats
```

## Estructura de DB

| Tabla | Propósito |
|---|---|
| `outbox_events` | PostgreSQL outbox local (no confundir con HANA) |
| `integration_logs` | Auditoría de todas las ejecuciones |
| `dead_letter_events` | Eventos que agotaron reintentos |
| `tenant_config` | Configuración multi-tenant |
| `tenant_feature_flags` | Feature flags por tenant |
| `integration_alerts` | Alertas operativas |
| `idempotency_records` | Guarda de idempotencia |
| `processed_messages` | Idempotencia de RabbitMQ consumers |
| `polling_cursors` | Marca de último ciclo de polling (price lists) |
| `price_snapshots` | Memoria de precios para detectar cambios en ITM1 |
| `vendor_bank_snapshots` | Línea base de cuentas bancarias de proveedores (flujo VENDOR_BANK_ALERT) |
