# iVend — Estructura de la petición para crear un pedido de venta

**Servicio:** `http://10.0.3.2/iVendAPI/iVendAPI.svc`
**Operación:** `IPostTransactionService.SaveTransaction`
**SOAPAction:** `http://www.iVend.com/IIntegrationService/2010/12/IPostTransactionService/SaveTransaction`
**Transporte:** SOAP 1.1, `Content-Type: text/xml; charset=utf-8`, `BasicHttpBinding`

---

## 1. Petición HTTP completa (lista para Postman / curl)

```http
POST /iVendAPI/iVendAPI.svc HTTP/1.1
Host: 10.0.3.2
Content-Type: text/xml; charset=utf-8
SOAPAction: "http://www.iVend.com/IIntegrationService/2010/12/IPostTransactionService/SaveTransaction"

<?xml version="1.0" encoding="utf-8"?>
<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
  <s:Body>
    <SaveTransaction xmlns="http://www.iVend.com/IIntegrationService/2010/12">
      <transaction>
        <GenerateIntegrationEvent>true</GenerateIntegrationEvent>
        <StoreId>TIENDA01</StoreId>
        <POSId>WEB</POSId>
        <UserId>ws.ecommerce</UserId>
        <TransactionDate>2026-10-02T14:30:00</TransactionDate>
        <BusinessDate>2026-10-02T00:00:00</BusinessDate>
        <CustomerRefNumber>WEB-2026-000123</CustomerRefNumber>
        <CustomerId>T1IVCL38499</CustomerId>
        <Comment>Pedido ecommerce</Comment>
        <SalesOrderList>
          <PostTransactionItem>
            <ProductId>T1IVPR0000001</ProductId>
            <Description>Producto de ejemplo</Description>
            <TaxCodeId>IVA</TaxCodeId>
            <Quantity>2</Quantity>
            <Price>150.00</Price>
            <OriginalPrice>150.00</OriginalPrice>
            <OriginalTaxCodeId>IVA</OriginalTaxCodeId>
          </PostTransactionItem>
        </SalesOrderList>
      </transaction>
    </SaveTransaction>
  </s:Body>
</s:Envelope>
```

> El `SOAPAction` debe ir **entre comillas dobles**. Omitirlas o errar el nombre de la interfaz
> (`IPostTransactionService`) da un error poco descriptivo.

---

## 1-bis. Valores reales verificados en la instalación

Estos valores salen de la base de producción (Grupo Roda) y ya están cargados en la colección
Postman. Usarlos evita que una prueba falle por un dato inventado en vez de por la estructura.

| Campo | Valor real | Origen |
|---|---|---|
| `StoreId` | `TIENDA01` (CASA MATRIZ) | `GetAllStores` |
| `POSId` | `T1POS11` — "T1 POS 11- DESPACHO" | `RtlPOS` (habilitado) |
| `UserId` | `1` | validado con `ValidateUser` |
| `CustomerId` | `T1IVCL38499` — CLIENTES GENERALES | `GetCustomer` |
| `ProductId` | `JRDMBLE010064` — MESA PLEGABLE 2.4X0.76M BLANCA SOL GARDEN | `InvProduct` (vendible, no exento) |
| `Price` / `OriginalPrice` | `1599.00` (lista `1`) | `PrcPriceMatrix` |
| `TaxCodeId` / `OriginalTaxCodeId` | `IVA` | `GetAllStores` |
| `BusinessDate` | `2026-10-02` | `GetAllStores` de TIENDA01 |

**POS disponibles en TIENDA01:** `T1POS01` … `T1POS13` (`T1POS11` = DESPACHO,
`T1POS13` = Atención Al Cliente). Todos `IsVirtualPOS = false`.

**Medios de pago reales** (`PmtPaymentType`, `IsDeleted = 0`):

| Id | Descripción | Type | TenderType a usar |
|---|---|---|---|
| `CASH` | CASH | 0 | `Cash` |
| `TCREDITO` | TARJETA DE CREDITO | 10 | `CreditCard` |
| `TDEBITO` | TARJETA DE DEBITO | 10 | `CreditCard` |
| `QR` | PAGO QR | 10 | `CreditCard` |
| `GIFTCARD` | CERTIFICADO DE REGALO | 3 | `GiftCertificate` |
| `USD` | USD | 7 | *inactivo* |
| `TRANSFERENCIA` | — | 10 | *inactivo* |

### Advertencias sobre estos valores

1. **No existe un POS virtual.** `T1POS11` es una terminal física: usarla para pedidos web hace
   que la venta aparezca en los reportes y la caja de esa terminal. Para producción conviene
   **crear un POS dedicado "WEB"**.
2. **El precio vive en `PrcPriceMatrix`.** `PrcSalesPrice` está vacía y `InvProduct.BasePrice`
   (1587.00 en este caso) **no coincide** con el precio de lista real (1599.00). Usar la matriz.
3. **`POSId` está en `RtlPOS.Id`, no en la clave.** El DTO espera el `Id` legible (`T1POS11`).
4. **`TIENDADC`** (centro de distribución) tiene `BusinessDate` de febrero 2025 — no enviar
   pedidos de venta ahí.

---

## 2. Las tres cosas que rompen el payload

### 2.1 El orden de los elementos es obligatorio

El contrato es un **XSD `sequence`**. Los elementos deben ir en el orden declarado, **con los
campos de la clase base primero**. Y el orden **no** es alfabético ni "el lógico":

- `<GenerateIntegrationEvent>` va **antes** que `<StoreId>` (es de `BaseWrapper`).
- `<CustomerRefNumber>` va **antes** que `<CustomerId>`.
- `<Comment>` va **después** de `<Total>`.
- En las líneas, `<Price>` va **después** de `<DiscountPercent>`, y `<OriginalPrice>` después de
  `<UOMId>`. Los campos obligatorios están **dispersos** en la secuencia.

Enviarlos desordenados produce un fallo de deserialización sin mensaje útil.

### 2.2 Dos namespaces distintos

| Namespace | Qué contiene |
|---|---|
| `http://www.iVend.com/IIntegrationService/2010/12` | Casi todo: `PostTransaction`, `PostTransactionItem`, `TransactionPayment`, `Store`, enums de negocio |
| `http://schemas.datacontract.org/2004/07/CXS.Retail.API` | Enums de API: `DiscountType` y variantes |
| `http://schemas.datacontract.org/2004/07/CXS.SubSystem.Payment` | `TenderType` |
| `http://schemas.datacontract.org/2004/07/CXS.SubSystem.Core` | `PaymentDetailType` |

El schema del servicio tiene `elementFormDefault="qualified"`, así que **los elementos van en el
namespace del servicio**; los enums se serializan como **texto plano** dentro de esos elementos
(confirmado con datos reales: `<StoreType>RetailStore</StoreType>`).

### 2.3 Valores nulos

Los campos `nillable` que quieras dejar vacíos van como:

```xml
<TransactionKey i:nil="true" xmlns:i="http://www.w3.org/2001/XMLSchema-instance" />
```

---

## 3. Campos de la cabecera (`transaction`)

Orden exacto de la secuencia. `R` = obligatorio, `—` = opcional.

| # | Elemento | R | Tipo | Notas |
|---|---|---|---|---|
| 1 | `GenerateIntegrationEvent` | — | bool | **`true` para que iVend lo propague a SAP B1** |
| 2 | `StoreId` | **R** | string | `TIENDA01` / `TIENDA02` / `TIENDA03` |
| 3 | `POSId` | **R** | string | Definir un POS "WEB" |
| 4 | `UserId` | **R** | string | Usuario iVend |
| 5 | `TransactionDate` | **R** | dateTime | Fecha/hora real |
| 6 | `BusinessDate` | **R** | dateTime | Fecha de negocio de la tienda |
| 7 | `TransactionId` | — | string | |
| 8 | `CustomerRefNumber` | — | string | **Nº de pedido web → clave de idempotencia** |
| 9 | `CustomerId` | **R** | string | Debe existir en iVend |
| 10 | `ContactId` | — | string | |
| 11 | `DiscountType` | — | enum | `None` / `Amount` / `Percent` |
| 12 | `DiscountAmount` | — | decimal | |
| 13 | `DiscountPercent` | — | decimal | |
| 14 | `SubTotal` | — | decimal | |
| 15 | `Total` | — | decimal | |
| 16 | `Comment` | — | string | |
| 17 | `IsSuspended` | — | bool | |
| 18 | `Validate` | — | bool | ⚠️ semántica **no documentada** — ver §6 |
| 19 | `PayableAmount` | — | decimal | |
| 20 | `TotalTaxableAmount` | — | decimal | |
| 21 | `SaleItemList` | — | array | |
| 22 | `RefundItemList` | — | array | |
| 23 | **`SalesOrderList`** | — | array | **← acá van las líneas del pedido** |
| 24 | `LayawayItemList` | — | array | |
| 25 | `SurchargeList` | — | array | |
| 26 | `Payments` | — | array | |
| 27 | `POSDocumentNumberSeriesKey` | — | string | Numeración |
| 28 | `CurrentNumberOfSeries` | — | string | |
| 29 | `UpdateCurrentNumberOfSeries` | — | bool | |
| 30 | `SalesOrderAdvanceAmount` | — | decimal | Anticipo |
| 31 | `LoyaltyId` | — | string | |
| 32 | `Tax` | — | decimal | |
| 33 | `DeliveryLocation` | — | string | |
| 34 | `TransactionCustomerName` | — | string | |
| 35 | `MPOSIdentifier` | — | string | |

*(Se omiten los `Integration1..Integration10` y los campos de cupón/cotización/entrega para
mantener la tabla legible; están entre el 1 y el 2 y después del 24 respectivamente.)*

---

## 4. Campos de la línea (`PostTransactionItem`)

| # | Elemento | R | Tipo | Notas |
|---|---|---|---|---|
| 1 | `ProductId` | **R** | string | Código de artículo en iVend |
| 2 | `Description` | **R** | string | |
| 3 | `TaxCodeId` | **R** | string | `IVA` en esta instalación |
| 4 | `Quantity` | **R** | decimal | |
| 5 | `TaxRate` | — | decimal | |
| 6 | `Tax` | — | decimal | |
| 7 | `SubTotal` | — | decimal | |
| 8 | `Total` | — | decimal | |
| 9 | `DiscountType` | — | enum | |
| 10 | `DiscountAmount` | — | decimal | |
| 11 | `DiscountPercent` | — | decimal | |
| 12 | `Price` | **R** | decimal | Precio unitario |
| 13 | `ManualDiscountType` | — | enum | |
| 14 | `ManualDiscountAmount` | — | decimal | |
| 15 | `SalesPerson` | — | complex | |
| 16 | `UOMQuantity` | — | decimal | |
| 17 | `UOMId` | — | string | |
| 18 | `OriginalPrice` | **R** | decimal | Precio antes de descuento |
| 19 | `FulfillmentPlanId` | — | string | `Alamacen` en esta instalación |
| 20 | `DeliveryDate` | — | dateTime | |
| 21 | `DeliveryLocation` | — | string | |
| 22 | `IsTaxFree` | — | bool | |
| 23 | `OriginalTaxCodeId` | **R** | string | |
| 24 | `Comment` | — | string | |

---

## 5. Enums útiles (valores exactos, case-sensitive)

| Enum | Valores |
|---|---|
| `DiscountType` | `None`, `Amount`, `Percent` |
| `TenderType` | `Cash`, `CreditCard`, `DebitCard`, `GiftCertificate`, `OnAccount`, `Check`, `TravellersCheck`, `ForeignCurrency`, `Voucher`, `Loyalty`, `Custom`, `StoreCredit`, `EBT` |
| `PaymentDetailType` | `None`, `GiftCertificate` |

---

## 6. Advertencias

1. **`Validate` no está documentado.** No asumir que `Validate = true` es un "dry run".
   Con alta probabilidad significa "aplicar validaciones de negocio antes de guardar", **no**
   "no guardar". Confirmar con soporte de iVend antes de usarlo como prueba.

2. **`TransactionKey` en `TransactionPayment` es obligatorio por esquema** aunque en un documento
   nuevo todavía no exista. Se envía como `i:nil="true"`.

3. **Nada de esto fue probado contra el servicio.** Los payloads fueron validados
   **estáticamente** (orden de secuencia, campos obligatorios, namespaces) contra el WSDL, pero
   **no ejecutados**, porque el único ambiente disponible es producción. Los valores de negocio
   —si iVend tolera `POSId=WEB`, qué `TaxRate` espera, si exige `Price` coincidente con la lista,
   si acepta un `CustomerId` genérico— **sólo se confirman en preproducción**.

4. **`GetAllStores` mostró `TIENDADC` con BusinessDate de febrero 2025.** No enviar pedidos web ahí.

---

## 7. Cómo verificar un payload sin tocar el servicio

```powershell
# Valida orden de secuencia, campos obligatorios y namespaces contra el WSDL
.\.zcode\ivend-probe\Test-iVendOrder.ps1 -PayloadPath .\mi-payload.xml
```

El validador fue verificado con una prueba negativa (detecta orden invertido, campos inexistentes
y obligatorios ausentes). Ejemplos incluidos:

| Archivo | Contenido |
|---|---|
| `.zcode/ivend-probe/payload-salesorder.xml` | Pedido mínimo → válido |
| `.zcode/ivend-probe/payload-salesorder-with-payment.xml` | Pedido + pago → válido |
| `.zcode/ivend-probe/payload-ROTO.xml` | Prueba negativa → 7 errores detectados |
