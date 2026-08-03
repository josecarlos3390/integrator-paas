using Integration.Shared.Dtos;

namespace Integration.Shared.Mappers;

/// <summary>
/// Transforms an invoice obtained from the SAP Service Layer to the format
/// expected by the external CRM API.
/// </summary>
public static class InvoiceMapper
{
    public static CrmInvoicePayload ToCrmPayload(SapInvoice sap)
    {
        if (!DateTime.TryParse(sap.DocDate, out var docDate))
            docDate = DateTime.MinValue;

        return new CrmInvoicePayload
        {
            ExternalId = sap.DocEntry.ToString(),
            InvoiceNumber = sap.DocNum.ToString(),
            CustomerId = sap.CardCode,
            CustomerName = sap.CardName,
            Date = docDate,
            TotalAmount = sap.DocTotal,
            Currency = sap.DocCurrency,
            LineItems = sap.DocumentLines.Select(l => new CrmInvoiceLineItem
            {
                Sku = l.ItemCode,
                Description = l.ItemDescription,
                Quantity = l.Quantity,
                Price = l.UnitPrice,
                Total = l.LineTotal
            }).ToList()
        };
    }
}
