using System.Text.Json.Serialization;

namespace Integration.Shared.Dtos;

/// <summary>
/// Payload sent to the SAP B1 Service Layer to create a sales order.
/// </summary>
public class SapOrderPayload
{
    [JsonPropertyName("CardCode")]
    public string CardCode { get; set; } = string.Empty;

    [JsonPropertyName("DocDate")]
    public string DocDate { get; set; } = string.Empty; // yyyy-MM-dd

    [JsonPropertyName("DocDueDate")]
    public string? DocDueDate { get; set; }

    [JsonPropertyName("NumAtCard")]
    public string NumAtCard { get; set; } = string.Empty; // referencia cruzada CRM

    [JsonPropertyName("Comments")]
    public string Comments { get; set; } = string.Empty;

    [JsonPropertyName("DocumentLines")]
    public List<SapOrderLine> DocumentLines { get; set; } = new();

    /// <summary>
    /// User-defined field to prevent CRM→SAP→CRM loops.
    /// Set to "CRM" when the order originates from the external CRM.
    /// </summary>
    [JsonPropertyName("U_SyncOrigin")]
    public string? USyncOrigin { get; set; }
}

public class SapOrderLine
{
    [JsonPropertyName("ItemCode")]
    public string ItemCode { get; set; } = string.Empty;

    [JsonPropertyName("Quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("UnitPrice")]
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("WarehouseCode")]
    public string? WarehouseCode { get; set; }
}
