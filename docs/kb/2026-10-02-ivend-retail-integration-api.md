---
title: "iVend Retail 6.6 Integration Service (iVendAPI.svc) — Integración con ecommerce vía SOAP y hallazgos de seguridad"
date: 2026-10-02
component: "iVend Retail 6.6 / IntegrationService (WCF SOAP 1.1)"
category: integration
severity: critical
status: open
sap_note: ""
tags:
  - ivend
  - retail
  - soap
  - wcf
  - sap-business-one
  - ecommerce
  - sales-order
  - security
  - integrator-paas
  - pos
scripts:
  - .zcode/ivend-probe/Invoke-iVendSoap.ps1
  - .zcode/ivend-probe/Invoke-iVendSearch.ps1
  - .zcode/ivend-probe/Test-iVendOrder.ps1
  - .zcode/ivend-probe/payload-salesorder.xml
  - .zcode/ivend-probe/payload-salesorder-with-payment.xml
  - docs/postman/iVend-Retail-Integration.postman_collection.json
related:
  - docs/ivend-retail-api-assessment.md
  - docs/ivend-sales-order-from-ecommerce.md
  - docs/ivend-salesorder-request-structure.md
---

# iVend Retail 6.6 Integration Service — Integración con ecommerce y hallazgos de seguridad

> **Nota:** este caso fue redactado para la base de conocimiento local pero **no pudo guardarse
> vía MCP**: las herramientas `hana_save_knowledge_case`, `hana_search_knowledge_base` y
> `hana_show_license_info` no responden (feature de KB no licenciada en el MCP server).
> Se deja en `docs/kb/` en el formato esperado por la KB para importarlo cuando se habilite.

---

## Entorno

| Dato | Valor |
|---|---|
| Endpoint | `http://10.0.3.2/iVendAPI/iVendAPI.svc` |
| Producto | iVend Retail **6.6.0.0** — Integration Service (WCF, `BasicHttpBinding`, SOAP 1.1) |
| Namespace | `http://www.iVend.com/IIntegrationService/2010/12` |
| Superficie | **385 operaciones** en **76 interfaces** |
| Compañía | **Grupo Roda** (producción, Bolivia) |
| ERP | **SAP Business One 9.2+** (`ERPSystemType = SAPBusinessOne_92AndAbove`) |
| BD de iVend | **SQL Server** (`NHibernate.Driver.SqlClientDriver`) |
| BD del ERP | **HANA** (`hanaroda25.gruporoda.com:30015`, schema `RETAIL`) |
| Tiendas | `TIENDA01` (CASA MATRIZ), `TIENDA02`, `TIENDA03`, `TIENDADC` |
| Config | IVA, lista de precios `1`, fulfillment plan `Alamacen` |

---

## Symptom

Se necesita integrar un ecommerce con iVend Retail para **registrar pedidos de venta** desde un
sistema externo (`integrator-paas`). Se requiere determinar si el webservice expone la operación,
cuál es la estructura exacta del payload SOAP, y si el endpoint es apto para uso productivo.

---

## Cause

### Funcional
La API **sí** permite crear pedidos de venta. La particularidad que confunde: **no existe un campo
`TransactionType`** — el tipo de documento lo determina **qué lista de ítems se puebla** dentro de
`PostTransaction`:

| Lista | Documento |
|---|---|
| `SalesOrderList` | **Pedido de venta** |
| `SaleItemList` | Venta directa (POS) |
| `Quotation` | Cotización |
| `LayawayItemList` | Apartado |

### Seguridad
El WSDL no declara ningún `soap:header` ni política de seguridad, y `BasicHttpBinding` no aplica
seguridad de transporte ni de mensaje. `ValidateUser` / `ValidateLicenseForIntegration` existen
pero **ninguna operación de negocio las exige**: son un chequeo de **licenciamiento**, no un
control de acceso.

---

## Solution

### Operación

```
POST http://10.0.3.2/iVendAPI/iVendAPI.svc
Content-Type: text/xml; charset=utf-8
SOAPAction: "http://www.iVend.com/IIntegrationService/2010/12/IPostTransactionService/SaveTransaction"
```

### Campos obligatorios

- **Cabecera `PostTransaction`:** `StoreId`, `POSId`, `UserId`, `TransactionDate`, `BusinessDate`, `CustomerId`
- **Línea `PostTransactionItem`:** `ProductId`, `Description`, `TaxCodeId`, `Quantity`, `Price`, `OriginalPrice`, `OriginalTaxCodeId`

### Clave para SAP

`GenerateIntegrationEvent = true` (heredado de `BaseWrapper`) hace que iVend genere el evento de
integración y **su conector existente lo propague a SAP B1**. Así el integrador queda como
**productor** hacia iVend y **no compite** por la cola de integración con el conector oficial.

Documentación oficial del campo: *"Generated the integration event for the ERP."*

### Tres trampas que rompen el payload

1. **ORDEN obligatorio.** El contrato es un XSD `sequence` con los campos de la clase base primero.
   `GenerateIntegrationEvent` va **antes** de `StoreId`; `CustomerRefNumber` **antes** de `CustomerId`;
   `Comment` **después** de `Total`. En las líneas, `Price` va después de `DiscountPercent` y
   `OriginalPrice` después de `UOMId`. **El orden no es alfabético ni lógico.**
2. **Dos namespaces.** Casi todo en `.../IIntegrationService/2010/12` (`elementFormDefault=qualified`).
   Los enums en `.../CXS.Retail.API` (`DiscountType`), `.../CXS.SubSystem.Payment` (`TenderType`),
   `.../CXS.SubSystem.Core` (`PaymentDetailType`). Los enums se serializan como **texto plano**.
3. **Nulos.** `i:nil="true"` con `xmlns:i="http://www.w3.org/2001/XMLSchema-instance"`.

### Enums

| Enum | Valores |
|---|---|
| `DiscountType` | `None`, `Amount`, `Percent` |
| `TenderType` | `Cash`, `CreditCard`, `DebitCard`, `GiftCertificate`, `OnAccount`, `Check`, `TravellersCheck`, `ForeignCurrency`, `Voucher`, `Loyalty`, `Custom`, `StoreCredit`, `EBT` |

### Remediación pendiente (bloqueante para producción)

1. Deshabilitar **directory browsing** en IIS para el sitio iVend.
2. Restringir `/iVendAPI` por firewall/origen (solo IP del integrador + red de tiendas).
3. **Terminar TLS** delante del servicio; deshabilitar o redirigir el puerto 80.
4. Cerrar **1433** salvo para administración.
5. `ShowStackTrace = false` y `ShowExtendedErrorInformation = false`.
6. Sacar `GetQueryResult` y `SearchTransaction` del alcance de cualquier integración.
7. **Rotar la cuenta de BD** y regenerar `Configuration.xml` cifrado.

---

## Evidence

### Lecturas sin credenciales

| Operación | Resultado |
|---|---|
| `CheckAPIConnection` | `true` |
| `GetVersionInformation` | `6.6.0.0,6.6.0.0` |
| `GetAllCountries` | 253 países |
| `GetAllStores` | 4 tiendas |
| `GetCustomer(T1IVCL38499)` | Ficha completa con **PII** (email, NIT, nombre, LoyaltyId) |
| `DeleteCountry(ZZZZ_NONEXISTENT_PROBE_9999)` | HTTP 200 — **escritura aceptada sin credenciales** |
| `GetEnterprise` | `CompanyName=Grupo Roda`, `ERPSystemType=SAPBusinessOne_92AndAbove` |

`DeleteCountry` fue verificado posteriormente como **no-op**: `GetCountry` con ese Id devuelve
`i:nil="true"` — el país nunca existió, no se borró ninguna fila.

### Credenciales

| Prueba | Resultado |
|---|---|
| `ValidateUser(UserId=1, Password=***)` | **`true`** |
| `ValidateLicenseForIntegration(..., IsDBTypeHANA=false)` | `false` |
| `ValidateLicenseForIntegration(..., IsDBTypeHANA=true)` | **`true`** |

El flag `IsDBTypeHANA` se refiere a la **base del ERP**, no a la de iVend. Con SAP B1 sobre HANA
corresponde `true`.

### SQL arbitrario — DOS operaciones

**a) `GetQueryResult(queryText)`** — ejecuta SQL crudo. Alcanzable sin autenticación. **No se usó.**

**b) `SearchTransaction(searhQuery, searchRecordsCount)`** — documentada como búsqueda de negocio,
pero **ejecuta el string como SQL crudo** vía ADO.NET `ExecuteReader`:

```
query vacío  → SOAP Fault: ExecuteReader: CommandText property has not been initialized
               (System.InvalidOperationException, Source=System.Data)
"SELECT 1 AS OK"  → HTTP 200, DataTable con <OK>1</OK>
INFORMATION_SCHEMA.TABLES  → listado de tablas
SELECT TOP 3 Id, FirstName, LastName FROM CusCustomer  → datos reales de clientes
SELECT TOP 3 TransactionKey, TransactionId, ActualDate, Total FROM TrxTransaction  → datos reales
```

Solo se ejecutaron `SELECT` de lectura, con autorización del usuario. `GetQueryResult` no se usó.
El fault de `ExecuteReader` es la prueba directa de que el string llega al motor de base de datos.
**Es más grave que `GetQueryResult`** porque está disfrazada de operación de negocio.

### Hallazgo crítico: las credenciales no protegen nada

Todo el sondeo —incluidas fichas de cliente con PII y los listados SQL— se obtuvo **sin enviar
autenticación**. Las credenciales válidas solo cambian el resultado de `ValidateLicense*`, no el
nivel de acceso. Cualquiera que alcance el endpoint tiene el mismo acceso que el super usuario.

### Hueco funcional: no se puede buscar un cliente

Entre las **385 operaciones** no existe `GetAllCustomers` ni `SearchCustomer`. Solo `GetCustomer(Id)`
por Id exacto. **No hay forma soportada de buscar cliente por email o NIT**, lo que es un problema
para ecommerce (deduplicación).

Salidas posibles:
- **A (recomendada):** tabla de correlación propia en el integrador (patrón `idempotency_records`).
- **B:** `SearchTransaction` con `SELECT` — funciona pero acopla al esquema físico y desaparece
  cuando se remedie la seguridad.
- **C:** `GetCustomer(Id)` con Ids generados por nosotros — el formato **varía por tienda**
  (`T1IVCL165267`, `T1C000036992`, `T1IVCL38499`).

### Tablas relevantes

| Tabla | Contenido |
|---|---|
| `CusCustomer` | Maestro de clientes |
| `TrxTransaction` | Cabecera (`HasOrders`, `HasQuotations`, `IsSuspended`, `FiscalDocumentNumber`) |
| `TrxTransactionOrder` | **Líneas de pedido** (`Quantity`, `OpenQuantity`, `Status`, `Type`) |
| `TrxTransactionSaleItem` | Líneas de venta |
| `TrxTransactionPayment` | Pagos |
| `TrxTransactionFulfillment` | Entregas |

### Exposición del endpoint

- `GET /iVendAPI` devuelve **listado de directorios**.
- `Configuration.xml` descargable: cadena de conexión SQL Server cifrada DPAPI,
  `ShowStackTrace=true`, `ShowExtendedErrorInformation=true`.
- **HTTPS roto**: puerto 443 abierto pero el handshake TLS falla → todo en claro.
- Puerto **1433** (SQL Server) abierto en red.
- CORS `Access-Control-Allow-Origin: *`.
- `/iVendAPI/New folder/System.Data.SQLite.dll` descargable; `/RWFTemp/` navegable.

### Rendimiento

`COUNT(*)` y consultas ordenadas por fecha sobre `TrxTransaction` **hicieron timeout**. Las
lecturas de historial deben ir por **rango de fecha acotado e indexado**, nunca escaneo completo.

### Calidad de datos

Un cliente tenía el nombre completo cargado en `FirstName`; otro sin `FirstName`. La deduplicación
no puede confiar en el nombre.

---

## Lessons

1. En iVend el tipo de documento **no se declara**: se infiere de qué lista de ítems se puebla.
   Buscar un campo `TransactionType` es perder el tiempo.
2. El WSDL de 1.34 MB con 385 operaciones es la fuente de verdad. Parsearlo programáticamente es
   más eficiente que la documentación pública de 2013, que además está incompleta.
3. El orden del XSD `sequence` es obligatorio y contraintuitivo (clase base primero). Enviar
   desordenado da un fallo de deserialización sin mensaje útil.
4. **Verificar siempre que un validador falla cuando debe.** Un `XmlSchemaSet` que no compila
   devuelve "VÁLIDO" sin haber validado nada: se obtuvo un **falso positivo** al validar contra los
   29 schemas embebidos (el schema de `System.Data` con `<xs:element ref="xs:schema"/>` rompe
   `Compile`). Se resolvió validando orden/obligatorios/namespaces contra las secuencias del WSDL,
   **con prueba negativa**.
5. El endpoint **sí devuelve el detalle del error** en el SOAP Fault, pero `Invoke-WebRequest` de
   PowerShell 5.1 lo descarta silenciosamente. Usar `System.Net.Http.HttpClient` para leer el
   cuerpo en HTTP 500 — ese detalle fue el que reveló que `SearchTransaction` ejecuta SQL.
6. Validar credenciales con **un solo intento** en producción: los fallos repetidos pueden
   bloquear la cuenta.
7. **Nunca ejecutar escrituras contra producción sin autorización explícita.** Se ejecutó un
   `DeleteCountry` con Id sintético **antes** de saber que era producción; resultó no-op
   verificable, pero fue un error de criterio.
8. Un flag como `IsDBTypeHANA` puede referirse a la base del **ERP** y no a la de la aplicación.
   Confirmar la semántica antes de reportar una contradicción.
9. Para integrar sin competir con el conector SAP existente: usar `GenerateIntegrationEvent=true`
   y dejar que iVend propague. **No** consumir la cola de integración con un segundo proceso.
10. Cuando la API no expone búsqueda de clientes, la solución correcta es una **tabla de
    correlación propia** en el integrador, no SQL crudo contra el esquema del proveedor.
