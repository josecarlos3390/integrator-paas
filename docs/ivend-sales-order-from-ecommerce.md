# Pedidos de venta desde ecommerce vía API de iVend

**Pregunta:** ¿Se pueden crear pedidos de venta en iVend desde un ecommerce usando la API?
**Respuesta:** **Sí.** La API expone exactamente esa operación. El bloqueante no es la API, es que
**no hay ambiente de pruebas**.

---

## 1. Mecanismo

La operación es:

```
IPostTransactionService.SaveTransaction(transaction: PostTransaction) → PostTransaction
```

`soapAction`:
`http://www.iVend.com/IIntegrationService/2010/12/IPostTransactionService/SaveTransaction`

### El detalle que hay que entender

**No existe un campo `TransactionType`.** El tipo de documento lo determina **qué lista de ítems
poblás** dentro de `PostTransaction`:

| Lista que se puebla | Documento resultante |
|---|---|
| `SaleItemList` | Venta directa (POS) |
| **`SalesOrderList`** | **Pedido de venta** ✅ |
| `Quotation` | Cotización |
| `LayawayItemList` | Apartado / layaway |
| `RefundItemList` | Devolución |
| `ExchangeItemList` | Cambio |
| `DeliveryItemList` | Entrega |
| `CouponIssueList` | Emisión de cupón |

Es decir: **no le decís "creá un pedido de venta", simplemente llenás `SalesOrderList`** y el motor
lo interpreta como tal. Cualquier intento de buscar un enumerado de tipo de transacción va a
fracasar — no está en el contrato.

---

## 2. Campos obligatorios

Extraídos del XSD (los que no declaran `minOccurs`, o sea `minOccurs=1`).

### `PostTransaction` (cabecera)

| Campo | Tipo | nillable |
|---|---|---|
| `StoreId` | string | sí |
| `POSId` | string | sí |
| `UserId` | string | sí |
| `TransactionDate` | dateTime | no |
| `BusinessDate` | dateTime | no |
| `CustomerId` | string | sí |

### `PostTransactionItem` (línea)

| Campo | Tipo | nillable |
|---|---|---|
| `ProductId` | string | sí |
| `Description` | string | sí |
| `TaxCodeId` | string | sí |
| `Quantity` | decimal | no |
| `Price` | decimal | no |
| `OriginalPrice` | decimal | no |
| `OriginalTaxCodeId` | string | sí |

> Que un campo sea `nillable` **no** significa que el servicio lo tolere vacío: es una restricción
> de esquema, no de negocio. Los obligatorios de negocio hay que confirmarlos contra preproducción.

---

## 3. Mapeo ecommerce → iVend

| Dato del ecommerce | Campo iVend | Nota |
|---|---|---|
| Nº de pedido web | `CustomerRefNumber` | Referencia cruzada; **clave natural de idempotencia** |
| Tienda destino | `StoreId` | `TIENDA01`, `TIENDA02`, `TIENDA03`, `TIENDADC` |
| Punto de venta | `POSId` | Necesario aunque el pedido no venga de un POS |
| Usuario que registra | `UserId` | Usuario iVend válido |
| Cliente | `CustomerId` | **Debe existir en iVend** → si es nuevo, `SaveCustomer` primero |
| Fecha | `TransactionDate`, `BusinessDate` | Ver §5 sobre cierre de negocio |
| Líneas | `SalesOrderList[]` | Ver §2 |
| Precio unitario | `Price` | Debe coincidir con la lista de precios de la tienda |
| Descuento | `DiscountType` + `DiscountAmount`/`DiscountPercent` | Enum: `None`, `Amount`, `Percent` |
| Impuesto | `TaxCodeId` + `TaxRate` | En esta instalación: `IVA` |
| Unidad de medida | `UOMId`, `UOMQuantity` | Opcional si es la UOM base |
| Anticipo | `SalesOrderAdvanceAmount` | Si el ecommerce cobró seña |
| Pago | `Payments[]` / `ARPaymentsList[]` | Ver §6 |
| Entrega | `TransactionFulfillments[]`, `FulfillmentPlanId`, `DeliveryDate`, `DeliveryLocation` | Plan de esta instalación: `Alamacen` |
| Comentario | `Comment` | |
| Propagar a SAP | `GenerateIntegrationEvent` | **Ver §4 — es la pieza clave** |

---

## 4. El puente hacia SAP Business One

`PostTransaction` hereda de `BaseWrapper`, que incluye:

```
GenerateIntegrationEvent : xs:boolean
```

La documentación oficial de iVend lo define textualmente como:

> *"Generated the integration event for the ERP."*

**Consecuencia para el diseño:** si enviamos el pedido con `GenerateIntegrationEvent = true`, iVend
genera el evento de integración y **su propio conector existente con SAP B1 lo propaga**. Nosotros
quedamos como **productor** hacia iVend y **no tocamos la cola de integración** — que era el riesgo
de competencia que señalé en el informe principal.

Esto es lo que hace que el enfoque sea limpio: el ecommerce → `integrator-paas` → `SaveTransaction`
→ iVend → *(conector existente)* → SAP B1.

⚠️ **Confirmar en preproducción** que el conector existente efectivamente toma el evento y crea el
pedido en SAP, y con qué tipo de documento (¿`Sales Order` de SAP B1? ¿`Quotation`?). Si el
`ERPSystemType = SAPBusinessOne_92AndAbove` está configurado para no propagar pedidos web, habría
que ajustar la configuración del conector, no la nuestra.

---

## 5. Fechas y cierre de negocio — ojo

`GetAllStores` mostró que **cada tienda tiene su propio `BusinessDate`**:

| Tienda | BusinessDate |
|---|---|
| TIENDA01 | 2026-10-02 |
| TIENDA02 | 2026-10-02 |
| TIENDA03 | 2026-10-02 |
| TIENDADC | **2025-02-07** |

Y el enterprise tiene `AllowBackDateAtEndOfDay = true`.

Un pedido web puede llegar **fuera del horario de la tienda o con la caja cerrada**. Antes de
implementar hay que definir:

- ¿Qué `BusinessDate` se envía para un pedido de madrugada?
- ¿Qué pasa si la tienda ya cerró el día? ¿Rebota o se registra en el día siguiente?
- `TIENDADC` tiene una fecha de negocio de **febrero 2025** — el centro de distribución parece
  inactivo para ventas. **No enviar pedidos de ecommerce ahí.**

---

## 6. Cobro

Si el ecommerce cobra con tarjeta o pasarela, hay tres caminos:

1. **Pedido sin cobro** (`SalesOrderAdvanceAmount = 0`) → el cliente paga al retirar. Es el más simple.
2. **Anticipo** (`SalesOrderAdvanceAmount > 0`) → registra la seña.
3. **Pago completo** vía `Payments[]` / `ARPaymentsList[]` → hay que mapear el medio de pago web a un
   `PaymentTypeId` de iVend. Requiere revisar los `GetPaymentType` de la instalación.

Además existe `IPostTransactionService.AuthorizePayment`, pero es para autorización contra el
procesador de pagos de iVend — **no** aplica si el cobro ya ocurrió en la pasarela del ecommerce.

---

## 7. Ciclo de vida del pedido

| Operación | Interfaz | Firma |
|---|---|---|
| Crear | `IPostTransactionService` | `SaveTransaction(transaction)` |
| Cerrar | `IPostTransactionService` | `SaleOrderClose(transactionKey)` |
| Cancelar | `IMobileIntegrationQueueService` | `CancelSalesOrder(udid, apiToken, transactionKey)` |
| Leer | `IIntegrationQueueService` | `SearchTransaction(searhQuery, searchRecordsCount)`, `GetTransaction(...)` |

> Nota: `CancelSalesOrder` pide `udid` + `apiToken` (credenciales de dispositivo móvil), a diferencia
> de `SaleOrderClose` que sólo pide la clave. **No** usar `MobileIntegrationQueueService` como ruta
> principal de ecommerce: está pensada para terminales móviles.

---

## 8. Qué necesitamos, concretamente

### Bloqueante

1. **Ambiente de preproducción de iVend.** No se puede desarrollar esto contra la producción de
   Grupo Roda. Un `SaveTransaction` mal armado puede crear un pedido corrupto que después impacta
   en SAP, en inventario y en caja. **Es la primera gestión a hacer.**

### Datos y accesos

2. **Usuario iVend** para el campo `UserId`, dedicado a integración y con privilegios mínimos
   (no el super usuario).
3. **`StoreId` + `POSId`** válidos para la tienda que recibe pedidos web. Salen de `GetAllStores`.
   Hay que definir si se crea un punto de venta dedicado "WEB".
4. **Estrategia de cliente:** ¿los clientes del ecommerce se crean en iVend? Si sí → `SaveCustomer`
   antes del pedido, y hay que definir la regla de deduplicación (por NIT / email / documento).
   ⚠️ **Ver §8-bis: la API no permite buscar un cliente por email o NIT.**
5. **Catálogo:** que cada SKU del ecommerce tenga su `ProductId` en iVend, su `TaxCodeId` (`IVA`) y
   su precio en la lista de la tienda (`PriceListId = 1`).
6. **Serie de numeración:** `POSDocumentNumberSeriesKey`, `CurrentNumberOfSeries`,
   `UpdateCurrentNumberOfSeries` — o `GetNextNumber(UserId, Password, TableName, SiteId)`.
   Hay que decidir quién asigna el número de documento para no chocar con el POS.
7. **Medios de pago** (`GetPaymentType`) si se registra el cobro.

### Decisiones de diseño

8. **Idempotencia:** usar `CustomerRefNumber` (nº de pedido web) como clave. Si el ecommerce
   reintenta, no debe crear dos pedidos.
9. **`Validate`:** el DTO tiene un flag `Validate : xs:boolean` cuya semántica exacta **no está
   documentada**. **No asumir que `Validate = true` es un "dry run"** — con alta probabilidad
   significa "aplicar validaciones de negocio antes de guardar", no "no guardar". Hay que
   confirmarlo con soporte de iVend **antes** de usarlo como prueba.
10. **Multi-tenant:** encaja con `TenantClientFactory`; credenciales en `tenant_config`, nunca en
    `appsettings.json` versionado.

---

## 8-bis. El hueco más importante: no se puede buscar un cliente

Verificado sobre las **385 operaciones** del contrato y contra la base real:

| Necesidad | ¿Existe operación? |
|---|---|
| Listar clientes | ❌ **No existe** `GetAllCustomers` ni `SearchCustomer` |
| Buscar cliente por email / NIT / documento | ❌ **No existe** |
| Obtener cliente por Id exacto | ✅ `ICustomerService.GetCustomer(Id)` |
| Crear / actualizar cliente | ✅ `SaveCustomer` |

Todo el sondeo encontró `GetAll*` para países, tiendas, grupos de descuento, grupos de precio,
bodegas, atributos… **pero no para clientes ni para transacciones.**

Consecuencia práctica para el ecommerce: cuando llega un pedido, **no hay forma soportada de
preguntar "¿este cliente ya existe en iVend?"**. Las salidas son:

**Opción A — tabla de correlación propia (recomendada).**
`integrator-paas` mantiene el mapeo `cliente_ecommerce ↔ CustomerId de iVend`. Es el mismo patrón
que ya se usa con `idempotency_records` / `polling_cursors`. Sin SQL crudo, sin acoplamiento al
esquema, y sobrevive a cambios de versión de iVend.

**Opción B — `SearchTransaction` con un `SELECT` (no recomendada).**
Funciona —se comprobó leyendo `CusCustomer`— pero:
- obliga a escribir SQL contra el esquema físico de iVend;
- acopla el integrador a tablas que iVend puede cambiar entre versiones;
- depende de una superficie de SQL arbitrario que **debería estar cerrada** (§4.3 del informe
  principal). Si se remedia la seguridad, esta opción desaparece.

**Opción C — `GetCustomer(Id)` con Ids generados por nosotros.** Sólo viable si el ecommerce genera
el `CustomerId` siguiendo la convención de la tienda. Ojo: **el formato de Id varía por tienda**
(`T1IVCL165267`, `T1C000036992`, `T1IVCL38499`), así que no se puede asumir un patrón fijo.

**Nota de calidad de datos:** en la muestra leída, un cliente tenía el nombre completo cargado en
`FirstName` (`CICERA MARIA DOS SANTOS PEREIRA`) y otro no tenía `FirstName`. La lógica de
deduplicación no puede confiar en que el nombre esté bien normalizado.

---

## 8-ter. Evidencia: lectura real de clientes y transacciones

Obtenida vía `SearchTransaction` (sólo lectura) para confirmar la capacidad:

```
-- Clientes
SELECT TOP 3 Id, FirstName, LastName FROM CusCustomer WHERE IsDeleted = 0
→ Id=T1IVCL165267  FirstName=CICERA MARIA DOS SANTOS PEREIRA
→ Id=T1C000036992  FirstName=INES CRUZ

-- Transacciones
SELECT TOP 3 TransactionKey, TransactionId, ActualDate, Total, HasOrders, IsSuspended FROM TrxTransaction
→ TransactionKey=00000718-b6f2-432c-80a8-6b8889e56fc4  TransactionId=T1SC100380726  Total=261.19996  HasOrders=false
→ TransactionKey=0000117d-8cdb-4b00-bf4f-7e7af4da0e51  TransactionId=T11597956  ActualDate=2024-11-29T12:34:53-04:00  Total=109.00002
→ TransactionKey=000026aa-7c48-4986-80d5-f3af2bfe3721  ActualDate=2022-09-28T18:21:20-04:00  Total=857.60009
```

Tablas relevantes confirmadas en el esquema:

| Tabla | Contenido |
|---|---|
| `CusCustomer` | Maestro de clientes |
| `TrxTransaction` | Cabecera de transacciones (`HasOrders`, `HasQuotations`, `IsSuspended`, `FiscalDocumentNumber`) |
| `TrxTransactionOrder` | **Líneas de pedido de venta** (`Quantity`, `OpenQuantity`, `Status`, `Type`) |
| `TrxTransactionSaleItem` | Líneas de venta |
| `TrxTransactionPayment` | Pagos |
| `TrxTransactionFulfillment` | Entregas |

⚠️ **Las consultas de conteo (`COUNT(*)`) y las ordenadas por fecha hicieron timeout** sobre
producción. La tabla de transacciones es grande y esos planes no son viables. Cualquier integración
que lea historial debe hacerlo **por rango de fecha acotado e indexado**, nunca con escaneos
completos.

---

## 9. Sobre el addon de SAP Business One

Cuando preguntás "¿a qué addon te referís?": la integración iVend ↔ SAP B1 **no** la hace el
`iVendAPI.svc` que estamos sondeando. Ese servicio es la **capa de API web sobre la base de iVend**.

La integración con SAP la hace el **conector de iVend para SAP Business One**, que corre del lado
de SAP y consume la cola de integración de iVend para generar los documentos en la compañía SAP.
Es lo que explica que `GetEnterprise` reporte `ERPSystemType = SAPBusinessOne_92AndAbove`.

**Cómo confirmar si está activo, sin tocar producción:**
- Revisar los servicios de Windows en el servidor de iVend (buscá servicios con "iVend" /
  "Integrat" en el nombre).
- Revisar los add-ons registrados en el cliente de SAP B1 (`Administración → Add-Ons`).
- Ver si hay documentos generados en SAP B1 que provengan de ventas de iVend (consultar
  `OPDN`/`ORDR` en SAP por origen iVend).

Si el conector está activo y propagando, **nuestro diseño del §4 funciona sin cambios**: nosotros
sólo producimos el pedido en iVend con `GenerateIntegrationEvent = true`.

---

## 10. Pendiente de seguridad (recordatorio)

Nada de esto cambia los hallazgos del informe principal: la API de producción está expuesta **sin
autenticación**, con examen de directorios habilitado, `GetQueryResult` ejecutando SQL crudo y
HTTPS roto. **Construir un conector de ecommerce contra ese endpoint, tal como está hoy, significa
que cualquiera en la red puede crear, leer y borrar pedidos y clientes.** La remediación del §4 del
informe principal no es opcional ni postergable: es prerequisito de este proyecto.
