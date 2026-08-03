namespace Integration.Shared.Dtos;

/// <summary>
/// Payload received from the external CRM to create a sales order in SAP.
/// </summary>
public class CrmOrderPayload
{
    public string CrmOrderId { get; set; } = string.Empty;
    public string CustomerId { get; set; } = string.Empty; // CardCode
    public DateTime OrderDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Warehouse { get; set; }
    public List<CrmOrderLine> Lines { get; set; } = new();
    public string? CallbackUrl { get; set; } // for async mode
}

public class CrmOrderLine
{
    public string Sku { get; set; } = string.Empty; // ItemCode
    public decimal Quantity { get; set; }
    public decimal Price { get; set; } // UnitPrice
    public string? Warehouse { get; set; }
}
