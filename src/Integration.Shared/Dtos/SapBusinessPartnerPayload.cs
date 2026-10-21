using System.Text.Json.Serialization;

namespace Integration.Shared.Dtos;

/// <summary>
/// Payload to create a business partner (customer) in SAP B1 on behalf of a
/// downstream consumer such as the WhatsApp bot.
/// </summary>
public class SapBusinessPartnerPayload
{
    [JsonPropertyName("CardCode")]
    public string CardCode { get; set; } = string.Empty;

    [JsonPropertyName("CardName")]
    public string CardName { get; set; } = string.Empty;

    [JsonPropertyName("CardType")]
    public string CardType { get; set; } = "cCustomer";

    [JsonPropertyName("Phone1")]
    public string? Phone1 { get; set; }

    /// <summary>RUT/NIT. Required by the Chilean localization of this company.</summary>
    [JsonPropertyName("FederalTaxID")]
    public string? FederalTaxID { get; set; }
}
