namespace Integration.Shared.Domain;

/// <summary>
/// Payload for price list header change events (OPLN).
/// </summary>
public class PriceListHeaderPayload
{
    public int ListNum { get; set; }
    public string ListName { get; set; } = string.Empty;
    public int? BaseNum { get; set; }
    public decimal? Factor { get; set; }
    public string? RoundSys { get; set; }
    public int? GroupCode { get; set; }
    public int? SppCounter { get; set; }
    public bool IsGrossPrc { get; set; }
    public DateTime? UpdateDate { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public string? PrimCurr { get; set; }
    public string? AddCurr1 { get; set; }
    public string? AddCurr2 { get; set; }
}
