using System.Text.Json.Serialization;

namespace Integration.Shared.Connectors.HansaCrm.Dtos;

/// <summary>
/// Root wrapper for every HansaCRM integration request.
/// The endpoint is always POST /data-ingestion-services; the "object" field discriminates the entity type.
/// </summary>
public class HansaCrmPayloadWrapper
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = string.Empty;

    public HansaCrmEntry Entry { get; set; } = new();
}

public class HansaCrmEntry
{
    public string Id { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public HansaCrmMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Some specs use "cliente" (Account/Vendor/Product) and others use "client" (Receivable/LimitCredit).
    /// </summary>
    [JsonPropertyName("cliente")]
    public HansaCrmCliente? Cliente { get; set; }

    [JsonPropertyName("client")]
    public HansaCrmCliente? Client { get; set; }

    public List<object> Messages { get; set; } = new();
}

public class HansaCrmMetadata
{
    [JsonPropertyName("batch_id")]
    public string BatchId { get; set; } = string.Empty;

    [JsonPropertyName("total_records")]
    public int TotalRecords { get; set; }

    [JsonPropertyName("batch_records")]
    public int BatchRecords { get; set; }

    [JsonPropertyName("batch_quantity")]
    public int BatchQuantity { get; set; }

    [JsonPropertyName("batch_number")]
    public int BatchNumber { get; set; }
}

public class HansaCrmCliente
{
    public string Profile { get; set; } = string.Empty;

    [JsonPropertyName("hcrm_id")]
    public string HcrmId { get; set; } = string.Empty;
}

// ------------------------------------------------------------------
// Vendor message (hansacrm_hbm_vendor_integrator)
// ------------------------------------------------------------------
public class HansaCrmVendorMessage
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Organization { get; set; } = string.Empty;

    [JsonPropertyName("sales_sector")]
    public List<HansaCrmSector> SalesSector { get; set; } = new();

    [JsonPropertyName("sales_channels")]
    public List<HansaCrmChannel> SalesChannels { get; set; } = new();

    [JsonPropertyName("sales_office")]
    public List<HansaCrmOffice> SalesOffice { get; set; } = new();

    public List<HansaCrmWarehouse> Warehouses { get; set; } = new();

    [JsonPropertyName("date_created")]
    public string DateCreated { get; set; } = string.Empty;

    [JsonPropertyName("date_modified")]
    public string DateModified { get; set; } = string.Empty;
}

public class HansaCrmSector
{
    public string Sector { get; set; } = string.Empty;
}

public class HansaCrmChannel
{
    public string Channel { get; set; } = string.Empty;
}

public class HansaCrmOffice
{
    public string Office { get; set; } = string.Empty;
}

public class HansaCrmWarehouse
{
    public string Warehouse { get; set; } = string.Empty;
}

// ------------------------------------------------------------------
// Account / BusinessPartner message (hansacrm_hbm_account_integrator)
// ------------------------------------------------------------------
public class HansaCrmAccountMessage
{
    public string Salutation { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("code1")]
    public string Code1 { get; set; } = string.Empty;

    [JsonPropertyName("credit_limit")]
    public decimal? CreditLimit { get; set; }

    [JsonPropertyName("email1")]
    public string Email1 { get; set; } = string.Empty;

    [JsonPropertyName("phone1")]
    public string Phone1 { get; set; } = string.Empty;

    [JsonPropertyName("phone2")]
    public string Phone2 { get; set; } = string.Empty;

    [JsonPropertyName("transport_zone")]
    public string TransportZone { get; set; } = string.Empty;

    [JsonPropertyName("market_area")]
    public string MarketArea { get; set; } = string.Empty;

    [JsonPropertyName("date_created")]
    public string DateCreated { get; set; } = string.Empty;

    [JsonPropertyName("date_modified")]
    public string DateModified { get; set; } = string.Empty;

    public List<HansaCrmAddress> Address { get; set; } = new();

    [JsonPropertyName("billing_info")]
    public List<HansaCrmBillingInfo> BillingInfo { get; set; } = new();

    [JsonPropertyName("sales_area")]
    public List<HansaCrmSalesArea> SalesArea { get; set; } = new();
}

public class HansaCrmAddress
{
    public string Type { get; set; } = string.Empty; // billing | shipping
    public string City { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
    public string Zone { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string Building { get; set; } = string.Empty;
    public string Roomnumber { get; set; } = string.Empty;
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
}

public class HansaCrmBillingInfo
{
    [JsonPropertyName("billing_name")]
    public string BillingName { get; set; } = string.Empty;

    [JsonPropertyName("doc_type")]
    public string DocType { get; set; } = string.Empty;

    [JsonPropertyName("billing_nro")]
    public string BillingNro { get; set; } = string.Empty;

    [JsonPropertyName("tax_system")]
    public string TaxSystem { get; set; } = string.Empty;
}

public class HansaCrmSalesArea
{
    public string Organization { get; set; } = string.Empty;

    [JsonPropertyName("sales_sector")]
    public string SalesSector { get; set; } = string.Empty;

    [JsonPropertyName("sales_channel")]
    public string SalesChannel { get; set; } = string.Empty;

    [JsonPropertyName("sales_office")]
    public string SalesOffice { get; set; } = string.Empty;

    [JsonPropertyName("sales_zone")]
    public string SalesZone { get; set; } = string.Empty;

    [JsonPropertyName("account_group")]
    public string AccountGroup { get; set; } = string.Empty;

    [JsonPropertyName("account_category")]
    public string AccountCategory { get; set; } = string.Empty;

    [JsonPropertyName("shipping_cond")]
    public string ShippingCond { get; set; } = string.Empty;

    [JsonPropertyName("supply_center")]
    public string SupplyCenter { get; set; } = string.Empty;

    [JsonPropertyName("trans_zone")]
    public string TransZone { get; set; } = string.Empty;

    [JsonPropertyName("payment_term")]
    public string PaymentTerm { get; set; } = string.Empty;

    [JsonPropertyName("credit_limit")]
    public decimal? CreditLimit { get; set; }

    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("price_group")]
    public string PriceGroup { get; set; } = string.Empty;

    [JsonPropertyName("price_list")]
    public string PriceList { get; set; } = string.Empty;

    [JsonPropertyName("invent_taking")]
    public string InventTaking { get; set; } = string.Empty;

    [JsonPropertyName("partner_functions")]
    public List<HansaCrmPartnerFunction> PartnerFunctions { get; set; } = new();
}

public class HansaCrmPartnerFunction
{
    public string Function { get; set; } = string.Empty;
    public int Counter { get; set; }

    [JsonPropertyName("code1")]
    public string Code1 { get; set; } = string.Empty;

    [JsonPropertyName("personal_num")]
    public int PersonalNum { get; set; }

    public string Contact { get; set; } = string.Empty;

    [JsonPropertyName("partner_cliente")]
    public string PartnerCliente { get; set; } = string.Empty;
}

// ------------------------------------------------------------------
// Receivable message (hansacrm_hbm_receivable_integrator)
// ------------------------------------------------------------------
public class HansaCrmReceivableMessage
{
    [JsonPropertyName("code1")]
    public string Code1 { get; set; } = string.Empty;

    public List<HansaCrmReceivableDetail> Receivable { get; set; } = new();
}

public class HansaCrmReceivableDetail
{
    public string Tcode { get; set; } = string.Empty;

    [JsonPropertyName("code1")]
    public string Code1 { get; set; } = string.Empty;

    [JsonPropertyName("date_document")]
    public string DateDocument { get; set; } = string.Empty;

    [JsonPropertyName("nro_document")]
    public string NroDocument { get; set; } = string.Empty;

    public string Reference { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Assignament { get; set; } = string.Empty;

    [JsonPropertyName("date_contab")]
    public string DateContab { get; set; } = string.Empty;

    public string Condition { get; set; } = string.Empty;

    [JsonPropertyName("date_assign")]
    public string DateAssign { get; set; } = string.Empty;

    [JsonPropertyName("days_arrears")]
    public string DaysArrears { get; set; } = string.Empty;

    public string Society { get; set; } = string.Empty;

    [JsonPropertyName("ref_invoice")]
    public string RefInvoice { get; set; } = string.Empty;

    [JsonPropertyName("text_head")]
    public string TextHead { get; set; } = string.Empty;

    [JsonPropertyName("date_base")]
    public string DateBase { get; set; } = string.Empty;

    [JsonPropertyName("class_document")]
    public string ClassDocument { get; set; } = string.Empty;

    public string Month { get; set; } = string.Empty;

    [JsonPropertyName("doc_bsad")]
    public string DocBsad { get; set; } = string.Empty;

    public string Compensation { get; set; } = string.Empty;

    [JsonPropertyName("year_comp")]
    public string YearComp { get; set; } = string.Empty;

    public string Account { get; set; } = string.Empty;
    public string Cebe { get; set; } = string.Empty;

    [JsonPropertyName("condition_days")]
    public string ConditionDays { get; set; } = string.Empty;

    [JsonPropertyName("condition_days2")]
    public string ConditionDays2 { get; set; } = string.Empty;

    [JsonPropertyName("condition_days3")]
    public string ConditionDays3 { get; set; } = string.Empty;

    [JsonPropertyName("reference_2")]
    public string Reference2 { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;
    public string An { get; set; } = string.Empty;

    [JsonPropertyName("anul_contab")]
    public string AnulContab { get; set; } = string.Empty;

    [JsonPropertyName("year_invoice")]
    public string YearInvoice { get; set; } = string.Empty;

    [JsonPropertyName("date_invoice")]
    public string DateInvoice { get; set; } = string.Empty;

    [JsonPropertyName("hours_invoice")]
    public string HoursInvoice { get; set; } = string.Empty;
}

// ------------------------------------------------------------------
// Product message (hansacrm_hbm_products_integrator)
// ------------------------------------------------------------------
public class HansaCrmProductMessage
{
    [JsonPropertyName("code1")]
    public string Code1 { get; set; } = string.Empty;

    [JsonPropertyName("code2")]
    public string Code2 { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("bar_code")]
    public string BarCode { get; set; } = string.Empty;

    [JsonPropertyName("supplier_code")]
    public string SupplierCode { get; set; } = string.Empty;

    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("unit_measure")]
    public string UnitMeasure { get; set; } = string.Empty;

    [JsonPropertyName("list_price")]
    public string ListPrice { get; set; } = string.Empty;

    [JsonPropertyName("global_stock")]
    public string GlobalStock { get; set; } = string.Empty;

    [JsonPropertyName("deletion_request")]
    public string DeletionRequest { get; set; } = string.Empty;

    [JsonPropertyName("blocking_date")]
    public string BlockingDate { get; set; } = string.Empty;

    [JsonPropertyName("date_created")]
    public string DateCreated { get; set; } = string.Empty;

    [JsonPropertyName("date_modified")]
    public string DateModified { get; set; } = string.Empty;

    public List<HansaCrmProductHierarchy> Hierarchy { get; set; } = new();
    public List<HansaCrmProductBatch> Batches { get; set; } = new();
}

public class HansaCrmProductHierarchy
{
    public string Organization { get; set; } = string.Empty;

    [JsonPropertyName("sales_sector")]
    public string SalesSector { get; set; } = string.Empty;

    [JsonPropertyName("sales_channel")]
    public string SalesChannel { get; set; } = string.Empty;

    [JsonPropertyName("supply_center")]
    public string SupplyCenter { get; set; } = string.Empty;

    public string Store { get; set; } = string.Empty;
    public string Stock { get; set; } = string.Empty;
    public string Hierarchy { get; set; } = string.Empty;
}

public class HansaCrmProductBatch
{
    [JsonPropertyName("batch_code")]
    public string BatchCode { get; set; } = string.Empty;

    [JsonPropertyName("batch_name")]
    public string BatchName { get; set; } = string.Empty;

    [JsonPropertyName("batch_stock")]
    public string BatchStock { get; set; } = string.Empty;

    [JsonPropertyName("date_due")]
    public string DateDue { get; set; } = string.Empty;

    [JsonPropertyName("date_created")]
    public string DateCreated { get; set; } = string.Empty;

    [JsonPropertyName("date_modified")]
    public string DateModified { get; set; } = string.Empty;
}

// ------------------------------------------------------------------
// Price List message (hansacrm_hbm_price_list_integrator)
// ------------------------------------------------------------------
public class HansaCrmPriceListMessage
{
    [JsonPropertyName("list_num")]
    public int ListNum { get; set; }

    [JsonPropertyName("card_code")]
    public string? CardCode { get; set; }

    [JsonPropertyName("is_full_sync")]
    public bool IsFullSync { get; set; }

    [JsonPropertyName("items")]
    public List<HansaCrmPriceListItem> Items { get; set; } = new();
}

public class HansaCrmPriceListItem
{
    [JsonPropertyName("item_code")]
    public string ItemCode { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("discount")]
    public decimal Discount { get; set; }

    [JsonPropertyName("valid_from")]
    public string? ValidFrom { get; set; }

    [JsonPropertyName("valid_to")]
    public string? ValidTo { get; set; }
}

// ------------------------------------------------------------------
// Price List Header message (hansacrm_hbm_price_list_header_integrator)
// ------------------------------------------------------------------
public class HansaCrmPriceListHeaderMessage
{
    [JsonPropertyName("list_num")]
    public int ListNum { get; set; }

    [JsonPropertyName("list_name")]
    public string ListName { get; set; } = string.Empty;

    [JsonPropertyName("base_num")]
    public int? BaseNum { get; set; }

    [JsonPropertyName("factor")]
    public decimal? Factor { get; set; }

    [JsonPropertyName("round_sys")]
    public string? RoundSys { get; set; }

    [JsonPropertyName("group_code")]
    public int? GroupCode { get; set; }

    [JsonPropertyName("is_gross_prc")]
    public bool IsGrossPrc { get; set; }

    [JsonPropertyName("valid_from")]
    public string? ValidFrom { get; set; }

    [JsonPropertyName("valid_to")]
    public string? ValidTo { get; set; }

    [JsonPropertyName("prim_curr")]
    public string? PrimCurr { get; set; }

    [JsonPropertyName("add_curr1")]
    public string? AddCurr1 { get; set; }

    [JsonPropertyName("add_curr2")]
    public string? AddCurr2 { get; set; }
}
