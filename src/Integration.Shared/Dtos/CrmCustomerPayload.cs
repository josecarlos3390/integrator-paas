namespace Integration.Shared.Dtos;

/// <summary>
/// Generic payload to synchronize a customer/vendor from SAP to an external CRM.
/// Includes all standard fields; the destination selects the ones it needs.
/// </summary>
public class CrmCustomerPayload
{
    // Identification
    public string ExternalId { get; set; } = string.Empty;          // CardCode
    public string Name { get; set; } = string.Empty;                // CardName
    public string? ForeignName { get; set; }                        // CardForeignName
    public string? Type { get; set; }                               // cCustomer | cSupplier | cLid
    public string? Alias { get; set; }
    public int? GroupCode { get; set; }
    public int? Series { get; set; }

    // Contact
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Phone2 { get; set; }
    public string? Mobile { get; set; }
    public string? Fax { get; set; }
    public string? Website { get; set; }

    // Fiscal
    public string? TaxId { get; set; }                              // FederalTaxID
    public string? VatGroup { get; set; }
    public string? VatIdNum { get; set; }
    public string? VatRegistrationNumber { get; set; }

    // Main Address
    public string? Country { get; set; }
    public string? City { get; set; }
    public string? County { get; set; }
    public string? State { get; set; }
    public string? ZipCode { get; set; }
    public string? AddressLine { get; set; }

    // Financial
    public string? Currency { get; set; }
    public decimal? CreditLimit { get; set; }
    public decimal? CurrentBalance { get; set; }
    public int? PayTermsGroupCode { get; set; }
    public int? PriceListNum { get; set; }
    public int? SalesPersonCode { get; set; }
    public decimal? DiscountPercent { get; set; }
    public decimal? CommissionPercent { get; set; }

    // Status
    public string? Status { get; set; }                             // active | inactive | frozen
    public bool? IsActive { get; set; }
    public bool? IsFrozen { get; set; }

    // UDFs
    public string? UdfOrigin { get; set; }
    public string? UdfDniType { get; set; }
    public string? UdfAjuste { get; set; }
    public string? UdfMoneda { get; set; }

    // Collections
    public List<CrmCustomerAddress> Addresses { get; set; } = new();
    public List<CrmCustomerContact> Contacts { get; set; } = new();

    // Metadata
    public DateTime? UpdatedAt { get; set; }
    public string? SourceSystem { get; set; } = "SAP";
}

public class CrmCustomerAddress
{
    public string? Name { get; set; }
    public string? Street { get; set; }
    public string? Block { get; set; }
    public string? ZipCode { get; set; }
    public string? City { get; set; }
    public string? County { get; set; }
    public string? Country { get; set; }
    public string? State { get; set; }
    public string? TaxCode { get; set; }
    public string? AddressType { get; set; } // billing | shipping
    public string? BuildingFloorRoom { get; set; }
    public string? Latitude { get; set; }
    public string? Longitude { get; set; }
}

public class CrmCustomerContact
{
    public int? InternalCode { get; set; }
    public string? Name { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Title { get; set; }
    public string? Position { get; set; }
    public string? Phone { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public bool? IsActive { get; set; }
}
