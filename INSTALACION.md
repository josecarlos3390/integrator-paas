# Guía de Instalación — Integration Bus

Este documento describe las dos formas de levantar la infraestructura del proyecto
(PostgreSQL, RabbitMQ, Seq) y la aplicación (API + Worker):

- **Opción A — Docker**: para desarrollo local en Windows 10/11, macOS o Linux.
- **Opción B — Windows Server nativo**: para servidores Windows Server sin Docker (producción/QA).

---

## Opción A — Docker (desarrollo local)

### Prerrequisitos

- Docker Desktop (Windows 10/11) o Docker Engine (Linux)
- .NET 8 SDK
- SAP HANA ODBC Driver (para conexión a HANA)

### Pasos

```powershell
# 1. Infraestructura (PostgreSQL, RabbitMQ, Seq)
docker-compose up -d

# 2. Migraciones EF Core
dotnet ef database update --project src\Integration.Shared --startup-project src\Integration.Api

# 3. API
dotnet run --project src\Integration.Api

# 4. Worker (en otra terminal)
dotnet run --project src\Integration.Worker
```

Servicios resultantes:

| Servicio | Puerto | Notas |
|---|---|---|
| PostgreSQL 16 | `localhost:5434` | usuario `integration`, DB `integration_bus` |
| RabbitMQ 3.13 | `localhost:5672` | consola: `http://localhost:15672` (guest/guest) |
| Seq | `localhost:5341` | UI: `http://localhost:8080` |

Para resetear el entorno: `docker-compose down -v`

> **Nota**: API y Worker NO se dockerizan porque requieren el SAP HANA ODBC
> client. Ver `Dockerfile.Api` y `Dockerfile.Worker` solo como referencia.

> **Nota**: Docker Desktop **no funciona en Windows Server**. En servidores
> Windows, usar la Opción B.

---

## Opción B — Windows Server nativo (sin Docker)

Probada en **Windows Server 2016 Standard** (build 14393). Válida también para
2019/2022.

### ¿Por qué no Docker en Windows Server?

- Docker Desktop no está soportado en Windows Server (ninguna versión).
- El motor disponible para Windows Server (Mirantis Container Runtime) solo
  ejecuta **contenedores Windows**, y las imágenes de este proyecto
  (`postgres:16-alpine`, `rabbitmq:3.13-management-alpine`, `datalust/seq`)
  son **Linux**, por lo que no correrían.
- LCOW (Linux containers on Windows) está deprecado y WSL2 no existe en
  Server 2016.

### Componentes a instalar

| Componente | Versión probada | Descarga |
|---|---|---|
| .NET SDK | 8.0.423 | `https://aka.ms/dotnet/8.0/dotnet-sdk-win-x64.exe` |
| .NET Framework | 4.8 (requerido por el instalador de Seq) | `https://go.microsoft.com/fwlink/?linkid=2088631` |
| PostgreSQL | 16.10 (instalador EDB) | `https://get.enterprisedb.com/postgresql/postgresql-16.10-1-windows-x64.exe` |
| Erlang/OTP | 26.2.5.21 | `https://github.com/erlang/otp/releases/download/OTP-26.2.5.21/otp_win64_26.2.5.21.exe` |
| RabbitMQ Server | 3.13.7 | `https://github.com/rabbitmq/rabbitmq-server/releases/download/v3.13.7/rabbitmq-server-3.13.7.exe` |
| Seq | 2026.1 | `https://datalust.co/Download/Begin?version=latest` |

> Todas las instalaciones requieren ejecutarse **como administrador** (elevar con UAC).

### Paso 1 — .NET 8 SDK

```powershell
.\dotnet-sdk-8-win-x64.exe /install /quiet /norestart
```

### Paso 2 — .NET Framework 4.8 (prerequisito del instalador de Seq)

```powershell
.\ndp48-x86-x64-allos-enu.exe /q /norestart
```

> El servidor debe **reiniciarse** después de este paso (código de salida 3010).
> Si el servidor ya tiene .NET Framework 4.8 activo, omitir.
> Verificar con:
> ```powershell
> Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' | Select Version, Release
> # Release >= 528040 equivale a .NET Framework 4.8
> ```

### Paso 3 — PostgreSQL 16 en puerto 5434

```powershell
.\postgresql-16.10-1-windows-x64.exe --mode unattended --unattendedmodeui none `
  --superpassword HoN3390 --serverport 5434 --servicename postgresql-x64-16 `
  --locale C --prefix "C:\Program Files\PostgreSQL\16" --datadir "C:\Program Files\PostgreSQL\16\data"
```

Crear el usuario y base de datos de la aplicación:

```powershell
$env:PGPASSWORD = 'HoN3390'
& 'C:\Program Files\PostgreSQL\16\bin\psql.exe' -U postgres -h localhost -p 5434 `
  -c "CREATE USER integration WITH PASSWORD 'HoN3390' CREATEDB;"
& 'C:\Program Files\PostgreSQL\16\bin\psql.exe' -U postgres -h localhost -p 5434 `
  -c "CREATE DATABASE integration_bus OWNER integration;"
```

> El puerto **5434** (no 5432) es deliberado, para evitar conflictos con otras
> instancias de PostgreSQL.

### Paso 4 — Erlang/OTP 26 (prerequisito de RabbitMQ)

```powershell
.\otp_win64_26.2.5.21.exe /S
```

> Instalar **siempre antes** que RabbitMQ (su instalador lo detecta vía registro).

### Paso 5 — RabbitMQ 3.13 + consola de administración

```powershell
.\rabbitmq-server-3.13.7.exe /S

# Habilitar la consola web (management)
& 'C:\Program Files\RabbitMQ Server\rabbitmq_server-3.13.7\sbin\rabbitmq-plugins.bat' enable rabbitmq_management

# Reiniciar el servicio para que el plugin tome efecto
Restart-Service RabbitMQ -Force
```

### Paso 6 — Seq

```powershell
msiexec /i Seq-2026.1.17083.msi /qn /norestart

# Registrar e iniciar el servicio (el MSI no lo registra por si solo)
& 'C:\Program Files\Seq\seq.exe' install
Start-Service Seq

# Primer arranque: opt-out de autenticacion (equivalente al docker-compose
# del proyecto, que no define credenciales). Para produccion, definir en
# cambio firstRun.adminPassword y reiniciar el servicio.
& 'C:\Program Files\Seq\seq.exe' config -k firstRun.noAuthentication -v true
Restart-Service Seq -Force
```

> Requiere .NET Framework 4.8 **activo** (ver Paso 2). Si el MSI devuelve error
> 1603, lo más probable es que falte el reinicio tras instalar .NET 4.8.
> Si el servicio queda en crash-loop con HTTP 503, revisar
> `C:\ProgramData\Seq\Logs\`: Seq 2024+ exige definir `firstRun.adminPassword`
> o `firstRun.noAuthentication` antes de poder arrancar por primera vez.
> Seq queda escuchando en `http://localhost:5341` (UI y API de ingesta).

### Paso 7 — Aplicación (migraciones, API, Worker)

```powershell
# Herramienta EF Core (local al repo, queda en .config/dotnet-tools.json)
dotnet new tool-manifest
dotnet tool install dotnet-ef --version 8.*

# Restaurar y compilar (por proyectos, ver "Problemas conocidos" #4)
dotnet restore src/Integration.Api/Integration.Api.csproj
dotnet restore src/Integration.Worker/Integration.Worker.csproj

# Migraciones
dotnet dotnet-ef database update --project src\Integration.Shared --startup-project src\Integration.Api

# Tablas que NO estan en migraciones EF (se crean con scripts del repo)
psql -U integration -h localhost -p 5434 -d integration_bus -f scripts/create-integration-requests-table.sql
psql -U integration -h localhost -p 5434 -d integration_bus -f scripts/create-tenant-quotas-table.sql
psql -U integration -h localhost -p 5434 -d integration_bus -f scripts/create-integration-metrics-table.sql
psql -U integration -h localhost -p 5434 -d integration_bus -f scripts/add-integration-logs-composite-indexes.sql

# Ejecutar (cada uno en su terminal)
dotnet run --project src\Integration.Api
dotnet run --project src\Integration.Worker
```

> Adicionalmente se requiere el **SAP HANA ODBC Driver** instalado en el
> servidor para que la API/Worker puedan conectarse a HANA (ver `Hana:ConnectionString`
> en `appsettings.json`). No forma parte de esta guía porque su instalador lo
> provee SAP.

---

## Problemas conocidos encontrados durante la instalación en Server 2016

1. **Start-Process con rutas que tienen espacios**: al pasar `-ArgumentList` como
   array, PowerShell une los valores con espacios *sin entrecomillar*, y
   `--prefix "C:\Program Files\..."` llega roto al instalador de PostgreSQL
   (exit code 1 sin log útil). Solución: pasar un único string con las comillas
   embebidas.
2. **Seq MSI error 1603**: el instalador requiere .NET Framework 4.8. El
   servidor venía con 4.7.2 (Release 461814). Instalar 4.8 y **reiniciar**;
   sin reinicio el MSI sigue fallando aunque el paquete ya esté copiado.
3. **rabbitmq_management no responde tras habilitarlo**: el plugin queda como
   "offline change"; hay que reiniciar el servicio RabbitMQ
   (`Restart-Service RabbitMQ -Force`).
4. **`Integration.slnx` no restaura con .NET SDK 8**: el formato `.slnx`
   requiere .NET SDK 9+. Con SDK 8, restaurar/compilar por archivo `.csproj`
   en lugar de la solución.
5. **`dotnet-ef` versión**: instalar `--version 8.*` para alinear con EF Core 8
   del proyecto (la última versión 10.x puede requerir runtime más nuevo).
6. **Seq arranca con HTTP 503 (crash-loop)**: Seq 2024+ exige definir
   `firstRun.adminPassword` o `firstRun.noAuthentication` en el primer arranque.
   El error exacto está en `C:\ProgramData\Seq\Logs\`. Además, el MSI instala
   los binarios pero no registra el servicio: hace falta `seq.exe install`.
7. **`relation "..." does not exist` en el Worker** (`integration_requests`,
   `tenant_quotas`, `integration_metric_counters`): estas tres tablas no están
   en las migraciones EF Core; se crean con los scripts de `scripts/` (ver
   Paso 7). Sin ellas, `IngestionWorker` falla en cada ciclo y los eventos
   de HANA agotan reintentos y caen a DLQ.
8. **`ERROR [IM002] ... Data source name not found` en el Worker**: falta el
   **SAP HANA ODBC Driver** (HDBODBC) en el servidor. Lo provee SAP (SAP HANA
   Client, descarga del SAP Support Portal). Hasta instalarlo, el flujo
   SAP→CRM (`OutboxDispatcherWorker`) y los endpoints que consultan HANA
   (p. ej. `/api/dashboard/stats`) devolverán este error. La API y el resto
   de workers funcionan con normalidad.
9. **`InvalidProgramException` de Dapper con HANA ODBC** (resuelto en código):
   pasar `object[]`/`List<object>` como `param` de `ExecuteAsync` hace que
   Dapper lo trate como multi-execute y genere IL inválido. Para parámetros
   posicionales (`?`) usar objetos anónimos o `DynamicParameters` agregados
   en orden. Corregido en `HanaOutboxRepository.AcquireLeaseAsync` y
   `DelayEventAsync` (2026-08-03).

---

## Verificación rápida

```powershell
# Servicios de Windows
Get-Service postgresql-x64-16, RabbitMQ, Seq

# Puertos
Test-NetConnection localhost -Port 5434   # PostgreSQL
Test-NetConnection localhost -Port 5672   # RabbitMQ AMQP
Test-NetConnection localhost -Port 15672  # RabbitMQ consola web
Test-NetConnection localhost -Port 5341   # Seq

# Aplicación
curl http://localhost:5000/health/live
curl http://localhost:5000/health/ready
```

## Credenciales del entorno

| Servicio | Usuario | Password | Alcance |
|---|---|---|---|
| PostgreSQL (superuser) | `postgres` | `HoN3390` | localhost:5434 |
| PostgreSQL (app) | `integration` | `HoN3390` | DB `integration_bus` |
| RabbitMQ | `guest` | `guest` | solo localhost (default) |
| Seq | sin auth por defecto | — | http://localhost:5341 |

> Estas credenciales son las mismas definidas en `docker-compose.yml` para
> desarrollo. Para producción, cambiarlas y actualizar `appsettings.json`.
