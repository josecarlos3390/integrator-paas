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

## Health Checks

- `/health/live` — proceso vivo (sin verificación de dependencias)
- `/health/ready` — PostgreSQL + SAP Service Layer accesibles
- Usar para readiness probes en Kubernetes/Docker Swarm.

## Graceful Shutdown

- `OutboxDispatcherWorker` espera hasta 30s antes de forzar cancelación del ciclo actual.
- No se inician nuevos ciclos después de recibir señal de stop.

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
