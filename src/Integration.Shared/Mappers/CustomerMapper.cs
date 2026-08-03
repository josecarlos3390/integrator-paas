using Integration.Shared.Dtos;

namespace Integration.Shared.Mappers;

/// <summary>
/// Maps a complete SAP B1 Business Partner to a generic CRM payload.
/// All SAP fields are available in the destination DTO; the external CRM
/// selects the ones it needs to consume.
/// </summary>
public static class CustomerMapper
{
    public static CrmCustomerPayload ToCrmPayload(SapBusinessPartner sap)
    {
        var payload = new CrmCustomerPayload
        {
            // Identification
            ExternalId = sap.CardCode,
            Name = sap.CardName,
            ForeignName = sap.CardForeignName,
            Type = sap.CardType,
            Alias = sap.AliasName,
            GroupCode = sap.GroupCode,
            Series = sap.Series,

            // Direct Contact
            Email = sap.EmailAddress,
            Phone = sap.Phone1,
            Phone2 = sap.Phone2,
            Mobile = sap.Cellular,
            Fax = sap.Fax,
            Website = sap.Website,

            // Fiscal
            TaxId = sap.FederalTaxID,
            VatGroup = sap.VatGroup,
            VatIdNum = sap.VatIDNum,
            VatRegistrationNumber = sap.VATRegistrationNumber,

            // Main Address (first available or BillTo)
            Country = sap.Country,
            City = sap.City,
            County = sap.County,
            State = sap.State,
            ZipCode = sap.ZipCode,
            AddressLine = sap.Address,

            // Financial
            Currency = sap.Currency,
            CreditLimit = sap.CreditLimit,
            CurrentBalance = sap.CurrentAccountBalance,
            PayTermsGroupCode = sap.PayTermsGrpCode,
            PriceListNum = sap.PriceListNum,
            SalesPersonCode = sap.SalesPersonCode,
            DiscountPercent = sap.DiscountPercent,
            CommissionPercent = sap.CommissionPercent,

            // Status
            Status = DetermineStatus(sap.Valid, sap.Frozen),
            IsActive = sap.Valid == "tYES",
            IsFrozen = sap.Frozen == "tYES",

            // UDFs
            UdfOrigin = sap.U_ORIGIN,
            UdfDniType = sap.U_B_dni_type,
            UdfAjuste = sap.U_Ajuste,
            UdfMoneda = sap.U_MONEDA,

            // Metadata
            UpdatedAt = CombineDateTime(sap.UpdateDate, sap.UpdateTime),
            SourceSystem = "SAP",

            // Collections
            Addresses = MapAddresses(sap.BPAddresses),
            Contacts = MapContacts(sap.ContactEmployees)
        };

        // If there is no main address but there are BPAddresses, we use the first one
        if (string.IsNullOrEmpty(payload.AddressLine) && payload.Addresses.Count > 0)
        {
            var first = payload.Addresses[0];
            payload.AddressLine = first.Street;
            payload.City = payload.City ?? first.City;
            payload.Country = payload.Country ?? first.Country;
            payload.State = payload.State ?? first.State;
            payload.ZipCode = payload.ZipCode ?? first.ZipCode;
        }

        return payload;
    }

    private static string DetermineStatus(string? valid, string? frozen)
    {
        if (frozen == "tYES") return "frozen";
        if (valid == "tYES") return "active";
        return "inactive";
    }

    private static DateTime? CombineDateTime(DateTime? date, string? time)
    {
        if (!date.HasValue) return null;
        if (string.IsNullOrWhiteSpace(time)) return date.Value;

        if (TimeSpan.TryParse(time, out var ts))
        {
            return date.Value.Date.Add(ts);
        }

        return date.Value;
    }

    private static List<CrmCustomerAddress> MapAddresses(List<SapBusinessPartnerAddress> sapAddresses)
    {
        return sapAddresses.Select(a => new CrmCustomerAddress
        {
            Name = a.AddressName,
            Street = a.Street,
            Block = a.Block,
            ZipCode = a.ZipCode,
            City = a.City,
            County = a.County,
            Country = a.Country,
            State = a.State,
            TaxCode = a.TaxCode,
            AddressType = a.AddressType == "bo_BillTo" ? "billing" : a.AddressType == "bo_ShipTo" ? "shipping" : a.AddressType?.ToLowerInvariant(),
            BuildingFloorRoom = a.BuildingFloorRoom,
            Latitude = a.U_LATITUDE,
            Longitude = a.U_LONGITUDE
        }).ToList();
    }

    private static List<CrmCustomerContact> MapContacts(List<SapContactEmployee> sapContacts)
    {
        return sapContacts.Select(c => new CrmCustomerContact
        {
            InternalCode = c.InternalCode,
            Name = c.Name,
            FirstName = c.FirstName,
            LastName = c.LastName,
            Title = c.Title,
            Position = c.Position,
            Phone = c.Phone1,
            Mobile = c.MobilePhone,
            Email = c.E_Mail,
            Address = c.Address,
            IsActive = c.Active == "tYES"
        }).ToList();
    }
}
