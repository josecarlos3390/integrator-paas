using Integration.Shared.Dtos;

namespace Integration.Shared.Mappers;

/// <summary>
/// Transforms an order received from the external CRM to the payload
/// required by the SAP B1 Service Layer to create a sales order.
/// </summary>
public static class OrderMapper
{
    public static SapOrderPayload ToSapPayload(CrmOrderPayload crm)
    {
        return new SapOrderPayload
        {
            CardCode = crm.CustomerId,
            DocDate = crm.OrderDate.ToString("yyyy-MM-dd"),
            DocDueDate = crm.DueDate?.ToString("yyyy-MM-dd"),
            NumAtCard = crm.CrmOrderId,
            Comments = $"CRM Order: {crm.CrmOrderId}",
            USyncOrigin = "CRM",
            DocumentLines = crm.Lines.Select(l => new SapOrderLine
            {
                ItemCode = l.Sku,
                Quantity = l.Quantity,
                UnitPrice = l.Price,
                WarehouseCode = l.Warehouse ?? crm.Warehouse
            }).ToList()
        };
    }
}
