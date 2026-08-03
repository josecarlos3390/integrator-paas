namespace Integration.Shared.Dtos;

/// <summary>
/// Payload sent to the external CRM when synchronizing an invoice from SAP.
/// </summary>
public class CrmInvoicePayload
{
    public string ExternalId { get; set; } = string.Empty; // DocEntry de SAP
    public string InvoiceNumber { get; set; } = string.Empty; // DocNum
    public string CustomerId { get; set; } = string.Empty; // CardCode
    public string CustomerName { get; set; } = string.Empty; // CardName
    public DateTime Date { get; set; }
    public decimal TotalAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<CrmInvoiceLineItem> LineItems { get; set; } = new();
}

public class CrmInvoiceLineItem
{
    public string Sku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Total { get; set; }
}
