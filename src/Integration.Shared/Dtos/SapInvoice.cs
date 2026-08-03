using System.Text.Json.Serialization;

namespace Integration.Shared.Dtos;

/// <summary>
/// Representation of an A/R invoice obtained from the SAP B1 Service Layer.
/// </summary>
public class SapInvoice
{
    [JsonPropertyName("DocEntry")]
    public int DocEntry { get; set; }

    [JsonPropertyName("DocNum")]
    public int DocNum { get; set; }

    [JsonPropertyName("CardCode")]
    public string CardCode { get; set; } = string.Empty;

    [JsonPropertyName("CardName")]
    public string CardName { get; set; } = string.Empty;

    [JsonPropertyName("DocDate")]
    public string DocDate { get; set; } = string.Empty; // yyyy-MM-dd

    [JsonPropertyName("DocTotal")]
    public decimal DocTotal { get; set; }

    [JsonPropertyName("DocCurrency")]
    public string DocCurrency { get; set; } = string.Empty;

    [JsonPropertyName("DocumentLines")]
    public List<SapInvoiceLine> DocumentLines { get; set; } = new();

    /// <summary>
    /// User-defined field to identify the document origin.
    /// If "CRM", the invoice was created by the external system and should not be synced back.
    /// </summary>
    [JsonPropertyName("U_SyncOrigin")]
    public string? U_SyncOrigin { get; set; }
}

public class SapInvoiceLine
{
    [JsonPropertyName("ItemCode")]
    public string ItemCode { get; set; } = string.Empty;

    [JsonPropertyName("ItemDescription")]
    public string ItemDescription { get; set; } = string.Empty;

    [JsonPropertyName("Quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("UnitPrice")]
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("LineTotal")]
    public decimal LineTotal { get; set; }
}
