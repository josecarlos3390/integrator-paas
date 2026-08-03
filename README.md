# Integration Bus — SAP ↔ CRM Integration Platform

Plataforma de integración enterprise que sincroniza datos entre SAP Business One (vía Service Layer) y un CRM externo. Utiliza un modelo de outbox sobre SAP HANA con polling prioritario, procesamiento asíncrono vía RabbitMQ, y un dashboard operativo en tiempo real.

## 🏗️ Arquitectura

```
┌─────────────┐      ┌─────────────┐      ┌─────────────┐
│   SAP B1    │◄────►│  HANA ODBC  │◄────►│   Worker    │
│  Service    │      │ OUTBOX_EVENTS│      │  (Poller)   │
│   Layer     │      └─────────────┘      └──────┬──────┘
└─────────────┘                                   │
                                                  ▼
┌─────────────┐      ┌─────────────┐      ┌─────────────┐
│  Mock/Real  │◄────►│  REST API   │◄────►│  PostgreSQL │
│    CRM      │      │  (Webhook)  │      │  (Logs/DLQ) │
└─────────────┘      └─────────────┘      └─────────────┘
       ▲                                              ▲
       │                                              │
       └──────────────────┬───────────────────────────┘
                          │
                    ┌─────────────┐
                    │   RabbitMQ  │
                    │   (Async)   │
                    └─────────────┘
```

### Flujos soportados

| Dirección | Eventos | Mecanismo |
|---|---|---|
| **SAP → CRM** | InvoiceCreated, CustomerCreated, CustomerUpdated | Polling HANA outbox + Worker |
| **CRM → SAP** | SalesOrderCreated | Sync HTTP (422) o Async RabbitMQ (202) |
| **CRM Callback** | order-result | Webhook HTTP POST |

## 🛠 Stack Tecnológico

- **.NET 8** — API REST + Worker Service
- **PostgreSQL 16** — Logs, DLQ, config de tenants, alertas, idempotencia
- **SAP HANA** — OUTBOX_EVENTS vía ODBC + Dapper
- **RabbitMQ 3.13** — Cola para pedidos async CRM→SAP
- **Polly v8** — Resilience (retry + circuit breaker) aislado por sistema
- **MassTransit** — Consumer de RabbitMQ
- **Refit** — Cliente HTTP tipado para CRM
- **Serilog + Seq** — Logs estructurados centralizados
- **EF Core 8** — Migrations y repositorios PostgreSQL

## 🚀 Cómo ejecutar

### Prerrequisitos

- Docker Desktop (o Docker Engine)
- .NET 8 SDK
- SAP HANA ODBC Driver (para conexión a HANA)

### 1. Infraestructura

```powershell
docker-compose up -d
```

Levanta:
- PostgreSQL en `localhost:5434`
- RabbitMQ en `localhost:5672` (management: `localhost:15672`)
- Seq en `localhost:5341` (UI: `localhost:8080`)

### 2. Migraciones

```powershell
dotnet ef database update --project src\Integration.Shared --startup-project src\Integration.Api
```

### 3. API

```powershell
dotnet run --project src\Integration.Api
```

- Swagger: `http://localhost:5000/swagger`
- Dashboard: `http://localhost:5000`
- Health live: `GET /health/live`
- Health ready: `GET /health/ready`

### 4. Worker

```powershell
dotnet run --project src\Integration.Worker
```

## ⚙️ Configuración clave

Editar `appsettings.json` en API y Worker:

```json
{
  "Sap": {
    "ServiceLayerUrl": "https://host:50000",
    "CompanyDB": "VIAGGIO_QA",
    "UserName": "jdiaz",
    "Password": "***"
  },
  "Hana": {
    "ConnectionString": "Driver={HDBODBC};SERVERNODE=host:30015;..."
  },
  "Postgres": {
    "ConnectionString": "Host=localhost;Port=5434;Database=integration_bus;..."
  },
  "Crm": {
    "BaseUrl": "http://localhost:5000"
  },
  "Outbox": {
    "PollingSeconds": 5,
    "BatchSize": 10,
    "MaxAttempts": 5
  },
  "Alerting": {
    "Enabled": true,
    "WebhookUrl": "",
    "DeadLetterThreshold": 5,
    "ErrorRateThreshold": 10
  },
  "Idempotency": {
    "Enabled": true,
    "TtlDays": 30
  }
}
```

## 🔑 Características implementadas

### 1. Priority-based Event Processing
Los eventos del outbox se ordenan por prioridad configurable (`EventPriority` en appsettings). `CustomerCreated` (10) se procesa antes que `InvoiceCreated` (5).

### 2. Feature Flags por Tenant
Activar/desactivar flujos por tenant sin redeploy. Dashboard UI con toggles visuales.

### 3. Multi-tenant Routing
Cada tenant puede apuntar a su propia instancia SAP y CRM vía `TenantConfig` en PostgreSQL.

### 4. Idempotencia Centralizada
Registro persistente `(TenantId, EventType, AggregateId)` evita duplicados en retries. TTL 30 días con cleanup automático.

### 5. Dead Letter Queue + Retry Automático
- 5 reintentos con backoff exponencial + jitter
- Business errors (404, BP not found) → DLQ manual
- Errores transitorios (5xx, timeout, circuit) → DLQ retry automático cada 15 min

### 6. Circuit Breaker Aislado
Pipelines separados de Polly para SAP y CRM. Un circuit breaker abierto en CRM no bloquea SAP.

### 7. Alertas Operativas
Alertas en tiempo real (DLQ, circuit breaker) + monitoreo periódico de thresholds. Dashboard con acknowledge.

### 8. Graceful Shutdown
El Worker espera hasta 30s a que el ciclo actual termine antes de morir.

## 📡 Endpoints principales

### Dashboard / Operaciones

| Método | Endpoint | Descripción |
|---|---|---|
| GET | `/api/dashboard/stats` | Métricas agregadas |
| GET | `/api/dashboard/events` | Eventos HANA con filtros |
| GET | `/api/dashboard/logs` | Logs de ejecución |
| POST | `/api/dashboard/retry?eventId=` | Reintentar evento |

### Administración

| Método | Endpoint | Descripción |
|---|---|---|
| GET | `/api/admin/tenants` | Listar tenants |
| GET | `/api/admin/features/{tenantId}` | Feature flags |
| POST | `/api/admin/features/{tenantId}/{key}` | Toggle feature |
| GET | `/api/admin/alerts` | Alertas activas |
| POST | `/api/admin/alerts/{id}/acknowledge` | Acknowledge alerta |

### Test / Simulación

| Método | Endpoint | Descripción |
|---|---|---|
| POST | `/api/test/simulate-invoice?docEntry=` | Simular InvoiceCreated |
| POST | `/api/test/simulate-customer?cardCode=&eventType=` | Simular Customer event |

### Health

| Método | Endpoint | Descripción |
|---|---|---|
| GET | `/health/live` | Proceso vivo |
| GET | `/health/ready` | Dependencias OK (Postgres + SAP) |

## 🧪 Test end-to-end

```powershell
# 1. Simular un cliente
curl -X POST "http://localhost:5000/api/test/simulate-customer?cardCode=C000004&eventType=CustomerCreated"

# 2. Ver dashboard
curl http://localhost:5000/api/dashboard/stats

# 3. Ver logs recientes
curl "http://localhost:5000/api/dashboard/logs?limit=10"

# 4. Health checks
curl http://localhost:5000/health/live
curl http://localhost:5000/health/ready
```

## 🐛 Troubleshooting

| Síntoma | Causa probable | Solución |
|---|---|---|
| "Missing X-Api-Key header" | Middleware de API Key activo | Usar header `X-Api-Key` o acceder a rutas públicas |
| "The circuit is now open" | CRM/SAP caído o lento | Esperar 30s (cooldown) o revisar health checks |
| Evento no se procesa | Feature flag desactivado | Verificar en dashboard / `/api/admin/features` |
| Duplicados en CRM | Idempotencia desactivada | Verificar `Idempotency.Enabled = true` |
| Worker no arranca | RabbitMQ no disponible | `docker-compose up -d` |

## 📁 Estructura del proyecto

```
src/
├── Integration.Api/          # ASP.NET Core API + Dashboard
│   ├── Controllers/          # REST endpoints
│   ├── HealthChecks/         # Postgres + SAP health checks
│   ├── Middleware/           # ApiKey, Tenant, CorrelationId
│   └── wwwroot/index.html    # Dashboard operativo
├── Integration.Worker/       # Background services
│   ├── Dispatchers/          # HanaOutboxDispatcher
│   └── Workers/              # Outbox, DLQ Retry, Alerting, Cleanup
└── Integration.Shared/       # Dominio + infraestructura compartida
    ├── Clients/              # SAP Service Layer, CRM (Refit)
    ├── Configuration/        # DTOs de config
    ├── Domain/               # Entidades EF Core
    ├── Dtos/                 # Payloads SAP/CRM
    ├── Infrastructure/       # DbContext, Polly, Migrations
    ├── Mappers/              # SAP → CRM
    ├── Messages/             # RabbitMQ messages
    ├── Repositories/         # PostgreSQL + HANA
    └── Services/             # Feature flags, Idempotencia, Alerting
```

## 🗺 Roadmap v2

- [ ] Métricas Prometheus + Grafana dashboards
- [ ] API versioning (`/api/v1/...`)
- [ ] Configuración dinámica sin restart (hot-reload)
- [ ] Retry masivo de DLQ desde dashboard
- [ ] Encriptación de secrets en reposo (AES-256)
- [ ] Soporte para SAP Service Layer OAuth

## 📄 Licencia

Proyecto interno Grupo Roda.
