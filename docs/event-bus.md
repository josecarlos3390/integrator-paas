# Bus de eventos SAP

## Objetivo

Desacoplar SAP de las aplicaciones consumidoras. El Worker de `integrator-paas`
lee el outbox de HANA, consulta el Service Layer, normaliza y publica un
contrato estable. El chatbot, el CRM y cualquier otra aplicación se suscriben
con su propia cola.

## Flujo

```text
SAP RETAIL_QA5
  ↓
INTEGRATION_BUS.OUTBOX_EVENTS
  ↓
Integration.Worker
  ├── lease / reintentos
  ├── normalización por tipo
  ├── caché de artículo y cliente
  └── publicación
        ↓
RabbitMQ sap.events (topic)
  ├── chatbot.sap.events
  └── crm.sap.events
```

## Contrato

```json
{
  "eventId": "AA2A00D0C4C39C151A00780047BFF73D",
  "tenantId": "RETAIL_QA5",
  "companyDb": "RETAIL_QA5",
  "eventType": "Updated",
  "objectType": "7",
  "aggregateId": "DCHRPCM160098",
  "schemaVersion": 1,
  "occurredAt": "2026-10-20T14:38:00Z",
  "data": {
    "sku": "DCHRPCM160098",
    "card_code": "*1",
    "periods": [
      {
        "price_list": 1,
        "valid_from": "2026-08-02",
        "valid_to": null,
        "price": 2331,
        "discount_percent": 10,
        "tiers": []
      }
    ]
  }
}
```

El consumidor no debe interpretar `keyColumns`, `keyValues` ni el
`AggregateId` compuesto. Esos detalles quedan en el PaaS.

## Routing

```text
{tenantId}.{objectType}.{eventType}
```

Ejemplos:

```text
RETAIL_QA5.7.Updated
RETAIL_QA5.13.Created
```

Bindings recomendados:

```text
chatbot.sap.events:
  RETAIL_QA5.4.*
  RETAIL_QA5.7.*
  RETAIL_QA5.13.*

crm.sap.events:
  RETAIL_QA5.2.*
  RETAIL_QA5.13.*
```

## Tipos

| Tipo | Entidad | Normalización |
|---|---|---|
| `2` | Cliente | `CardCode`, `CardName`, `FederalTaxID` |
| `4` | Artículo | precio de lista, stock por tienda |
| `7` | Precio especial | períodos y escalones por cantidad |
| `13` | Factura | cabecera y líneas |
| `17` | Pedido | pendiente de normalizar |

## Configuración

```json
"Outbox": {
  "PublishEvents": true,
  "EventExchange": "sap.events",
  "PublishObjectTypes": ["2", "4", "7", "13"]
}
```

No publicar movimientos de inventario. El stock se consulta por API con caché.

## Colas

Cada aplicación debe tener una cola durable distinta:

```text
chatbot.sap.events
crm.sap.events
otra-app.sap.events
```

Nunca compartir una cola entre dos consumidores: RabbitMQ repartiría los
mensajes y cada aplicación recibiría solo una parte.

## Idempotencia

Cada consumidor debe guardar `event_id` antes de aplicar el cambio. El chatbot
usa `sap_event_inbox`; el CRM debe tener su propia tabla o usar
`processed_messages`.

## Operación

```powershell
# Ver colas
docker exec integration-rabbitmq rabbitmqctl list_queues name messages_ready messages_unacknowledged

# Ver bindings
docker exec integration-rabbitmq rabbitmqctl list_bindings source_name destination_name routing_key

# Reencolar un evento (solo pruebas)
# Actualizar PROCESSED_AT = NULL, LEASED_UNTIL = NULL en HANA
```

## Pendientes

- Normalizar `17` pedidos.
- Cola CRM para `17`.
- Saldo de cuenta corriente.
- Pagos y notas de crédito.
- Métricas por tenant y consumidor.
- `ConfigureConsumeTopology = false` para eliminar bindings `#` residuales.
