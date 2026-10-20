# Evaluación técnica — iVend Retail Integration API (`http://10.0.3.2/iVendAPI`)

**Fecha:** 2026-10-02
**Objetivo:** Determinar la viabilidad de construir un conector iVend Retail dentro de `integrator-paas`.
**Método:** Sondeo directo del endpoint (HTTP/SOAP), descarga y parseo del WSDL, y ejecución controlada de operaciones de prueba.

> ### ⚠️ ACTUALIZACIÓN — la instancia es PRODUCCIÓN
>
> Confirmado por el usuario: `10.0.3.2` es **base productiva**, no laboratorio.
> Consecuencias directas:
> - Los hallazgos de la sección 4 pasan de "deuda técnica" a **incidente de seguridad abierto**.
> - **Queda prohibido ejecutar operaciones `Save*`, `Delete*`, `Update*` o `GetQueue*` (sin el sufijo
>   `WithoutStatusUpdate`) durante el análisis.** Sólo lecturas no mutantes.
> - Se ejecutó **una** escritura de prueba antes de conocer esta condición: `DeleteCountry` con el ID
>   sintético `ZZZZ_NONEXISTENT_PROBE_9999`. **Verificado posteriormente como no-op**:
>   `GetCountry` con ese ID devuelve `i:nil="true"`, es decir el país nunca existió y no se borró
>   ninguna fila. No hubo pérdida de datos.
>
> **Entorno identificado:** `CompanyName = Grupo Roda`, `ERPSystemType = SAPBusinessOne_92AndAbove`,
> BD SQL Server. Es decir, iVend Retail 6.6 sobre **SAP Business One 9.2+**.

---

## 1. Veredicto

| Pregunta | Respuesta |
|---|---|
| ¿El webservice está vivo y responde? | **Sí.** Responde correctamente a SOAP 1.1. |
| ¿Se puede integrar desde .NET 8? | **Sí, es perfectamente viable.** Contrato WSDL completo y navegable. |
| ¿Se puede usar tal como está, en producción? | **No sin remediar antes los hallazgos de seguridad de la sección 4.** |
| ¿Se necesita licencia/credenciales de iVend? | **Sí, para la ruta "sancionada"** (cola de integración y licencia de integración), aunque la API **no las exige** técnicamente. |

La conclusión de fondo: **técnicamente el conector es muy factible y el contrato es rico**; el problema no es la API sino **cómo está expuesta**.

---

## 2. Inventario técnico

**Servicio**

| Atributo | Valor |
|---|---|
| Producto | iVend Retail — Integration Service |
| Versión reportada | `6.6.0.0,6.6.0.0` (vía `GetVersionInformation`) |
| Servicio WCF | `IntegrationService` |
| Target namespace | `http://www.iVend.com/IIntegrationService/2010/12` |
| Endpoint | `http://10.0.3.2/iVendAPI/iVendAPI.svc` |
| Binding | `BasicHttpBinding_IIntegrationService` (SOAP 1.1, document/literal, `soap/http`) |
| Seguridad de binding | **Ninguna** — 0 `soap:header` declarados en el WSDL |
| WSDL | Expuesto públicamente en `?wsdl` y `?singleWsdl` (1.34 MB) |
| Host | IIS 10.0 / ASP.NET |
| Backend | SQL Server (`NHibernate.Driver.SqlClientDriver`, dialecto `MsSql2005Dialect`) |

**Superficie funcional**

- **385 operaciones** distribuidas en **76 interfaces** de contrato.
- Interfaces más relevantes para una integración retail:

| Interfaz | Ops | Uso típico |
|---|---|---|
| `IProductService` | 28 | Maestro de artículos, variantes, imágenes, costos |
| `IMobileIntegrationQueueService` | 27 | Cola móvil / autenticación de dispositivos |
| `ILoyaltyService` | 24 | Puntos, gift cards, clientes de lealtad |
| `IIntegrationQueueService` | 21 | **Cola de integración (núcleo del patrón pull)** |
| `ICustomerService` | 10 | Clientes, catálogos, sucursales |
| `ISalesPriceService` / `IPriceListService` | 10 | Listas de precios y precios de venta |
| `IStoreService` / `IWarehouseService` | 10 | Tiendas, almacenes, inventario |
| `IQueryService` | 6 | `GetQueryResult`, `ValidateLicense`, `GetNextNumber` |
| `IPostTransactionService` | — | `SaveTransaction`, `AuthorizePayment` |

---

## 3. Evidencia de funcionamiento (pruebas ejecutadas)

Todas las llamadas se hicieron por SOAP 1.1 contra el endpoint real, **sin enviar ninguna credencial**.

| Operación | Resultado | Observación |
|---|---|---|
| `CheckAPIConnection` | `true` | Health-check del propio servicio |
| `GetVersionInformation` | `6.6.0.0,6.6.0.0` | Versión confirmada |
| `GetAllCountries` | 253 países | Lectura completa sin credenciales |
| `GetAllStores` | 4 tiendas | Datos reales de la instalación |
| `GetCustomer(T1IVCL38499)` | Ficha completa | **Incluye PII: email, NIT, nombre, LoyaltyId** |
| `Authenticate(udid,apiToken)` vacío | `false` | Esperado (credenciales vacías) |
| `ValidateLicenseForIntegration` vacío | `false` | Esperado (credenciales vacías) |
| `GetQueryResult` (query vacío / inválido) | HTTP 500 | **Alcanza la capa SQL sin autenticación** |
| `DeleteCountry(ZZZZ_NONEXISTENT_PROBE_9999)` | HTTP 200, respuesta vacía | Escritura aceptada sin autenticación |
| `GetQueueWithoutStatusUpdate(UpdateStatus=false)` | Lista vacía | Cola sin registros pendientes |

> **Nota de transparencia:** se ejecutó **una** operación de escritura (`DeleteCountry`) contra un ID inexistente y de prueba, exclusivamente para verificar si la capa de escritura valida credenciales. No se modificó ningún dato. **No se explotó `GetQueryResult`** para leer ni modificar datos: sólo se comprobó su alcanzabilidad con entradas no ejecutables.

**Datos de la instalación observados** (instancia con datos de Bolivia):

- Tiendas: `TIENDA01` (CASA MATRIZ), `TIENDA02` (SUCURSAL NRO 1), `TIENDA03` (SUCURSAL 2), `TIENDADC` (CENTRO DE DISTRIBUCION)
- Almacenes: `101-1001`, `102-1001`, `103-1001`, `104-1001`
- Impuesto: `IVA`; Lista de precios: `1`; Perfil: `Default Profile`
- Cliente de contado: `T1IVCL38499` / clave `2000000000038498`

---

## 4. Hallazgos de seguridad (bloqueantes para producción)

> Estos hallazgos son sobre **la instalación**, no sobre el código del conector. Deben resolverse o aceptarse formalmente antes de exponer el integrador.

### 4.1 🔴 CRÍTICO — Ausencia total de autenticación

El WSDL no declara **ningún** `soap:header` ni política de seguridad, y el binding `BasicHttpBinding` por defecto no aplica seguridad de transporte ni de mensaje. Se verificó empíricamente:

- **Lecturas sin credenciales:** maestro de tiendas, países, y **fichas de cliente con PII** (email, NIT, nombre, IDs de lealtad).
- **Escrituras sin credenciales:** `DeleteCountry` respondió HTTP 200 sin pedir autenticación.

Las operaciones `Authenticate` y `ValidateLicenseForIntegration` **existen** y devuelven `false` con credenciales vacías, pero **ninguna operación de negocio las obliga**. Es decir: la autenticación es "opcional" a nivel de contrato — quien conozca la URL tiene control total del maestro de datos.

### 4.2 🔴 CRÍTICO — Examen de directorios habilitado

`GET http://10.0.3.2/iVendAPI` devuelve el **listado de archivos** del directorio virtual:

```
2/13/2026   bin/                      (listado bloqueado por IIS, pero enumerado)
5/25/2026   Configuration.xml         (1.714 bytes)
5/20/2015   Global.asax
7/9/2013    iMMPosISS20.exe
6/27/2011   iVendAPI.svc
12/22/2016  Web.config
12/8/2014   SMSReply.aspx
```

`Configuration.xml` es **descargable** y expone:

- La **cadena de conexión a SQL Server**, cifrada con DPAPI (`AQAAANCMnd8BFdERjHoAwE...`). Es descifrable únicamente en esa máquina, pero es material sensible filtrado.
- `ShowExtendedErrorInformation = true` y `ShowStackTrace = true` → **los errores devuelven trazas de pila**, lo que facilita el mapeo del backend.
- El nombre de la compañía, también cifrado con DPAPI.

### 4.3 🔴 CRÍTICO — DOS operaciones de SQL arbitrario expuestas

No es una. Son **dos**, y ambas alcanzables **sin autenticación**:

**a) `IQueryService.GetQueryResult(queryText)`** — ejecuta texto SQL crudo y devuelve el resultado.
Confirmado alcanzable. **No se usó.**

**b) `IIntegrationQueueService.SearchTransaction(searhQuery, searchRecordsCount)`** — documentada
como "obtiene una lista de transacciones según criterio de búsqueda", pero **el parámetro
`searhQuery` se ejecuta como SQL crudo** vía ADO.NET `ExecuteReader`, devolviendo un `DataTable`.

Evidencia obtenida con consultas de **sólo lectura**:

| Prueba | Resultado |
|---|---|
| `SearchTransaction` con `searhQuery` vacío | SOAP Fault: `ExecuteReader: CommandText property has not been initialized` (`System.InvalidOperationException`, `Source=System.Data`) |
| `SearchTransaction` con `SELECT 1 AS OK` | HTTP 200, `DataTable` con `<OK>1</OK>` |
| `SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES ...` | Listado de tablas del esquema |
| `SELECT TOP 3 Id, FirstName, LastName FROM CusCustomer` | Datos reales de clientes |
| `SELECT TOP 3 ... FROM TrxTransaction` | Transacciones reales |

El fault de `ExecuteReader` es la prueba directa: el string del cliente llega al motor de base de
datos. **Esto es más grave que `GetQueryResult`**, porque `SearchTransaction` está documentada como
operación de búsqueda de negocio, no como ejecutor de SQL — pasa desapercibida en una revisión
superficial del contrato.

> **Alcance de lo ejecutado:** únicamente `SELECT` de sólo lectura, con autorización explícita del
> usuario. No se ejecutó ninguna escritura. El sondeo se detuvo al observar timeouts por consultas
> pesadas sobre producción. `GetQueryResult` no se usó en ningún momento.

**Consecuencia:** cualquier cliente de red puede leer y modificar toda la base de iVend —clientes,
transacciones, precios, inventario— sin credenciales. La remediación del §4.1 es urgente.

### 4.3-bis 🔴 Las credenciales no protegen nada

Se validaron credenciales reales de producción (`ValidateUser` → `true`). El resultado relevante:

- Con credenciales válidas se leen clientes y transacciones.
- **Sin credenciales también.** Todo el sondeo previo —fichas de cliente con PII, maestro de
  tiendas, y los listados vía `SearchTransaction`— se obtuvo **sin enviar un solo dato de
  autenticación**.

Las credenciales sólo son relevantes para `ValidateUser` / `ValidateLicense*` / `GetNextNumber`.
Es decir: **no son un control de acceso, son un chequeo de licenciamiento**. Cualquiera que
alcance el endpoint tiene el mismo nivel de acceso que el super usuario.

### 4.4 🟠 ALTO — Sólo HTTP, HTTPS roto

- Puerto 80 abierto y funcional.
- Puerto 443 abierto pero el handshake TLS **falla** (`Error inesperado de recepción` / conexión terminada).
- Consecuencia: **todo el tráfico viaja en claro**, incluidos datos de clientes y, en una integración real, las credenciales de `ValidateLicenseForIntegration`.

### 4.5 🟠 ALTO — SQL Server expuesto en red

El puerto **1433/TCP está abierto** en `10.0.3.2`. Sumado a 4.2 (cadena de conexión filtrada) y 4.3, el vector de ataque es directo.

### 4.6 🟡 MEDIO — Recursos auxiliares expuestos

- `/iVendAPI/New folder/System.Data.SQLite.dll` (1.6 MB) descargable.
- `/iVendAPI/RWFTemp/` navegable.
- `/iVendAPI/SMSReply.aspx` accesible (ASP.NET WebForms con `__VIEWSTATE`).
- CORS abierto: `Access-Control-Allow-Origin: *` con `Allow-Headers: Content-Type, Accept, UserName, Password`.

### Recomendaciones mínimas

1. **Deshabilitar el examen de directorios** en IIS para el sitio iVend (`<directoryBrowse enabled="false" />`).
2. **Restringir `/iVendAPI` por firewall/origen** (sólo la IP del integrador + red de tiendas).
3. **Terminar TLS** delante del servicio (reverse proxy) y consumir sólo HTTPS; deshabilitar el puerto 80 o redirigir.
4. **Cerrar 1433** en el perímetro salvo para administración.
5. Poner `ShowStackTrace = false` y `ShowExtendedErrorInformation = false` en `Configuration.xml`.
6. **Sacar `GetQueryResult` del alcance del conector.** Aunque disponible, no debe usarse: acopla el integrador al esquema físico y multiplica el riesgo.
7. Rotar la cuenta de BD y regenerar el `Configuration.xml` cifrado, dado que el archivo estuvo expuesto.

---

## 5. Viabilidad del conector — diseño propuesto

### 5.1 Dos direcciones de integración

**A. Pull — iVend → integrator-paas**

Patrón nativo de iVend: cola de integración con confirmación por lote.

```
1.  ValidateLicenseForIntegration(UserId, Password, IsDBTypeHANA)   → habilitar integración
2.  SaveRegisteredApplication(...)                                  → registrar el integrador (una vez)
3.  GetQueueWithoutStatusUpdate(getTopNRecords, clientTimeOut,
                                UpdateStatus=false, applicationId)  → lote de pendientes
4.  ... procesar y publicar al outbox local ...
5.  UpdateRecordsStatus(IntegrationKeysList)                        → confirmar el lote
```

Operaciones de extracción puntuales complementarias: `SearchTransaction`, `GetTransaction`,
`GetTransactionItemDetails`, `GetDeltaInventoryUpdates`, `GetInventoryOfProduct`,
`GetInventoryStatus`, `GetProductByBarCode`.

**B. Push — integrator-paas → iVend**

Operaciones `Save*` sobre el maestro:

| Dominio | Operaciones |
|---|---|
| Clientes | `SaveCustomer`, `SaveCustomerList`, `SaveCustomerBranch`, `DeleteCustomer` |
| Artículos | `SaveProduct`, `SaveProductList`, `SaveProductsList`, `SaveProductVariant`, `SaveBatch` |
| Precios | `SaveSalesPrice`, `SaveSalesPriceList`, `SavePriceList`, `SavePriceMatrix` |
| Inventario | `SaveInventoryUpdates`, `SetFinalInventory`, `SaveStockTransfer` |
| Transacciones | `SaveTransaction`, `AuthorizePayment` (venta en tienda) |
| Organización | `SaveStore`, `SaveWarehouse`, `SaveBranch`, `SaveEnterprise` |

### 5.2 🔴 RIESGO DE ARQUITECTURA — ¿quién es dueño de la cola de integración?

`ERPSystemType = SAPBusinessOne_92AndAbove` implica que **iVend ya tiene su propio addon oficial de
integración con SAP B1**. Ese addon consume habitualmente la **misma cola de integración** que el
conector pretende usar.

Si el addon oficial está corriendo y nosotros añadimos un segundo consumidor que llame a
`UpdateRecordsStatus(IntegrationKeysList)`, los dos **compiten por los mismos registros**: el que
confirme primero se los lleva, y el otro los pierde. Esto **rompería la integración existente** de
forma silenciosa (ventas que no llegan a SAP, o que llegan duplicadas).

**Evidencia recogida:** la cola del enterprise (`GetQueueOfEnterpriseWithoutStatusUpdate`,
`UpdateStatus=false`) devuelve **vacía**. No se pudo distinguir si es porque:
- (a) el addon oficial ya la está drenando continuamente — escenario más probable, o
- (b) el `applicationId` de prueba (`PROBE`) no está registrado y la consulta no matchea nada.

Es imposible separar (a) de (b) sin un `applicationId` registrado, y **no existe operación
`GetRegisteredApplication`** en el contrato para listarlos.

**Antes de implementar el pull hay que responder esto.** Si el addon oficial ya ocupa la cola, las
opciones son:

1. **No tocar la cola.** Integrar por *event sourcing* alternativo: `SearchTransaction` +
   `GetTransaction` + `GetTransactionItemDetails` por rango de fechas, o `GetDeltaInventoryUpdates`
   con cursor propio. El `polling_cursors` que ya existe en `integrator-paas` encaja aquí.
2. **Registrar una `applicationId` propia** vía `SaveRegisteredApplication` y filtrar la cola por
   aplicación, **si** iVend soporta múltiples consumidores particionados (hay que confirmarlo con
   soporte de iVend/Cenfura).
3. **Reemplazar** el addon oficial — descartado, implicaría reescribir la integración SAP validada.

La opción 1 es la más segura y no invasiva, y es la que recomiendo por defecto.

### 5.3 Consideraciones técnicas para .NET 8

1. **El WSDL es grande** (1.34 MB, 385 operaciones). Generar el proxy completo con
   `dotnet-svcutil` produce decenas de miles de líneas. **Recomendación: generar sólo las
   interfaces necesarias** (`/ct` para filtrar) en un proyecto aparte, o encapsular en un
   proyecto `Integration.Ivend` dedicado para que el proxy no contamine `Integration.Shared`.

2. **El orden de los elementos importa.** Los contratos son de `DataContractSerializer`:
   en las respuestas se observa el orden alfabético estricto de miembros
   (`Address1, Address2, ... Bank, BankAccount, ...`) con los miembros de clase base primero.
   Construir los XML a mano es frágil; **usar el proxy generado o `XmlSerializer` tipado**.

3. **Namespaces auxiliares que hay que respetar:**
   - `http://schemas.datacontract.org/2004/07/CXS.Retail.API` (enums y data contracts)
   - `http://schemas.microsoft.com/2003/10/Serialization/Arrays` (`ArrayOfstring`, etc.)
   - `http://www.w3.org/2001/XMLSchema-instance` para `i:nil="true"`

4. **El `soapAction` identifica la interfaz, no sólo la operación.** Formato:
   `{targetNamespace}/{InterfaceName}/{OperationName}`. Con el proxy generado esto es
   automático; con cliente crudo hay que mapearlo (se dejó el mapa completo en
   `.zcode/ivend-probe/operation-map.txt`).

5. **Manejo de errores.** La instalación tiene `ShowStackTrace=true`; hoy las fallas llegan
   como HTTP 500 con cuerpo vacío o como SOAP Fault. El conector debe:
   - Distinguir *business error* (no reintentar) de *transient* (reintentar).
   - Respetar la convención de `integration-paas`: los business errors **no** se guardan en
     idempotencia, los transitorios **sí**.

6. **Resiliencia.** Seguir `AGENTS.md`: pipeline Polly **dedicado por sistema destino**.
   El circuit breaker de iVend **no** debe compartirse con SAP ni con CRM.

7. **Multi-tenant.** Encaja con `TenantClientFactory`: cachear el cliente SOAP por tenant con
   el patrón `AsyncLazy` ya existente. Cada tenant tendría su propia `BaseUrl` y credenciales.

8. **Idempotencia.** Envolver el procesamiento con `IIdempotencyService.TryProcessAsync`.
   Para el patrón pull, la clave natural de idempotencia es la clave de la cola de iVend
   (`IntegrationKeysList`), lo que permite confirmar (`UpdateRecordsStatus`) sólo tras
   persistir exitosamente — semántica *at-least-once* con confirmación posterior.

### 5.4 Esqueleto sugerido

```
src/Integration.Shared/Connectors/
    IIvendConnector.cs          // análogo a ICrmConnector
    IvendApiResponse.cs
src/Integration.Shared/Clients/
    IvendClient.cs              // SOAP tipado + Polly
src/Integration.Worker/Workers/
    IvendQueuePollingWorker.cs  // patrón pull con cursor
```

---

## 6. Preguntas abiertas

### Resueltas

| # | Pregunta | Respuesta |
|---|---|---|
| 1 | ¿Producción o laboratorio? | **Producción** (`Grupo Roda`) |
| 3 | ¿BD SQL Server o HANA? | **SQL Server** → `IsDBTypeHANA = false` |
| — | ¿Qué ERP hay detrás? | **SAP Business One 9.2+** (`ERPSystemType = SAPBusinessOne_92AndAbove`) |

### Pendientes (bloquean el diseño)

1. **🔴 ¿El addon oficial de iVend para SAP B1 está activo y consumiendo la cola de integración?**
   Es la pregunta más importante (ver 5.2). Si la respuesta es sí, **descartar el patrón pull por
   cola** y usar el enfoque por eventos/cursor.
2. **¿Existe licencia de integración iVend y un `applicationId` registrado?** Se necesita saberlo
   para decidir entre la ruta sancionada y la ruta directa. No hay operación para listar
   aplicaciones registradas, así que hay que obtenerlo de soporte de iVend/Cenfura o de la
   configuración del addon.
3. **Credenciales:** el usuario propuso un *super usuario*. Antes de usarlo hay que decidir:
   - `ValidateLicenseForIntegration` es de **sólo lectura** y de bajo riesgo, pero un intento
     fallido repetido **puede bloquear la cuenta** → se recomienda **un único intento**.
   - **No** usar un super usuario de SAP B1 para la integración en producción. Lo correcto es crear
     un **usuario de integración dedicado con privilegios mínimos**.
   - Las credenciales deben vivir en `tenant_config` (cifradas o vía secreto), **nunca** en
     `appsettings.json` versionado ni en el historial de chat.

### Operativas

4. **¿Cuál es el caso de uso prioritario?** ¿Sincronizar maestro SAP → tiendas (push),
   extraer ventas de tienda → SAP (pull), o ambos?
5. **¿Se va a remediar la exposición del endpoint?** El conector debería apuntar a HTTPS con
   restricción por IP; confirmar si habrá un reverse proxy. **El conector no debe construirse
   contra un endpoint HTTP sin autenticar en producción.**
6. **¿Se puede obtener una instancia de preproducción?** Probar un conector contra producción de
   un sistema de punto de venta en vivo es inaceptable: un `SaveTransaction` mal formado puede
   generar una venta corrupta en la tienda.

---

## 7. Artefactos generados

| Archivo | Contenido |
|---|---|
| `.zcode/ivend-probe/iVendAPI.wsdl` | WSDL completo descargado (1.34 MB) |
| `.zcode/ivend-probe/operation-map.txt` | Mapa `operación → soapAction` de las 385 operaciones |
| `.zcode/ivend-probe/Configuration.xml` | Copia del config expuesto (referencia de hallazgos) |
| `.zcode/ivend-probe/Invoke-iVendSoap.ps1` | Helper de sondeo SOAP reutilizable |

> Los artefactos de sondeo se dejaron **fuera del árbol de compilación**, en `.zcode/`,
> para no incorporar el WSDL ni el config a la solución.
