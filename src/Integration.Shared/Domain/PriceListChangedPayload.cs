namespace Integration.Shared.Domain;

/// <summary>
/// Payload stored in the Outbox for price list change events.
/// Supports both list prices (ITM1) and special prices/discounts (OSPP+SPP1+SPP2).
/// </summary>
public class PriceListChangedPayload
{
    public int ListNum { get; set; }
    public string? CardCode { get; set; }
    public string GroupBy { get; set; } = "ListNum"; // ListNum | CardCode
    public List<PriceListItem> Items { get; set; } = new();
    public bool IsFullSync { get; set; }
    public int BatchIndex { get; set; }
    public int BatchCount { get; set; }
    public DateTime SyncDate { get; set; }
}

public class PriceListItem
{
    public string ItemCode { get; set; } = string.Empty;
    public string? CardCode { get; set; }
    public int ListNum { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Discount { get; set; }

    // OSPP fields
    public int? OsppLogInstanc { get; set; }
    public DateTime? OsppUpdateDate { get; set; }
    public bool OsppAutoUpdt { get; set; }
    public bool OsppExpand { get; set; }

    // SPP1 period lines
    public List<Spp1Period>? Periods { get; set; }

    // SPP2 quantity discounts
    public List<Spp2QuantityDiscount>? QuantityDiscounts { get; set; }
}

public class Spp1Period
{
    public int LineNum { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Discount { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public bool AutoUpdt { get; set; }
    public bool Expand { get; set; }
}

public class Spp2QuantityDiscount
{
    public int Spp1LineNum { get; set; }
    public int Spp2LineNum { get; set; }
    public decimal Amount { get; set; }
    public decimal Price { get; set; }
    public string Currency { get; set; } = string.Empty;
    public decimal Discount { get; set; }
    public int? UomEntry { get; set; }
}
