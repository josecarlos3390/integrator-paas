using Integration.Shared.Connectors.HansaCrm.Dtos;
using Integration.Shared.Domain;
using Integration.Shared.Dtos;

namespace Integration.Shared.Connectors.HansaCrm;

/// <summary>
/// Maps SAP BusinessPartner payloads to HansaCRM ingestion formats.
/// Supports Account (customer) and Vendor (supplier) object types.
/// </summary>
public static class HansaCrmMapper
{
    public static HansaCrmPayloadWrapper MapAccount(CrmCustomerPayload sap, HansaCrmDefaults config)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var created = sap.UpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? now;

        var message = new HansaCrmAccountMessage
        {
            Code1 = sap.ExternalId ?? string.Empty,
            Name = sap.Name ?? string.Empty,
            Salutation = string.Empty,
            Email1 = sap.Email ?? string.Empty,
            Phone1 = sap.Phone ?? string.Empty,
            Phone2 = sap.Phone2 ?? string.Empty,
            CreditLimit = sap.CreditLimit,
            TransportZone = string.Empty,
            MarketArea = string.Empty,
            DateCreated = created,
            DateModified = now,
            Address = MapAddresses(sap.Addresses),
            BillingInfo = MapBillingInfo(sap),
            SalesArea = MapSalesArea(sap, config)
        };

        return new HansaCrmPayloadWrapper
        {
            Object = "hansacrm_hbm_account_integrator",
            Entry = new HansaCrmEntry
            {
                Id = Guid.NewGuid().ToString(),
                Date = now,
                Metadata = CreateMetadata(1, 1, 1, 1),
                Cliente = new HansaCrmCliente { Profile = config.Organization ?? string.Empty, HcrmId = string.Empty },
                Messages = new List<object> { message }
            }
        };
    }

    public static HansaCrmPayloadWrapper MapVendor(CrmCustomerPayload sap, HansaCrmDefaults config)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var created = sap.UpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? now;

        var message = new HansaCrmVendorMessage
        {
            Code = sap.ExternalId ?? string.Empty,
            Name = sap.Name ?? string.Empty,
            Organization = config.Organization ?? string.Empty,
            SalesSector = (config.SalesSectors ?? new List<string>())
                .Select(s => new HansaCrmSector { Sector = s }).ToList(),
            SalesChannels = (config.SalesChannels ?? new List<string>())
                .Select(c => new HansaCrmChannel { Channel = c }).ToList(),
            SalesOffice = (config.SalesOffices ?? new List<string>())
                .Select(o => new HansaCrmOffice { Office = o }).ToList(),
            Warehouses = (config.Warehouses ?? new List<string>())
                .Select(w => new HansaCrmWarehouse { Warehouse = w }).ToList(),
            DateCreated = created,
            DateModified = now
        };

        return new HansaCrmPayloadWrapper
        {
            Object = "hansacrm_hbm_vendor_integrator",
            Entry = new HansaCrmEntry
            {
                Id = Guid.NewGuid().ToString(),
                Date = now,
                Metadata = CreateMetadata(1, 1, 1, 1),
                Cliente = new HansaCrmCliente { Profile = config.Organization ?? string.Empty, HcrmId = string.Empty },
                Messages = new List<object> { message }
            }
        };
    }

    public static HansaCrmPayloadWrapper MapReceivable(CrmInvoicePayload sap, HansaCrmDefaults config)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var docDateStr = sap.Date.ToString("yyyyMMdd");

        var message = new HansaCrmReceivableMessage
        {
            Code1 = sap.CustomerId ?? string.Empty,
            Receivable = new List<HansaCrmReceivableDetail>
            {
                new()
                {
                    Tcode = "BSID",
                    Code1 = sap.CustomerId ?? string.Empty,
                    DateDocument = docDateStr,
                    NroDocument = sap.InvoiceNumber ?? string.Empty,
                    Reference = sap.ExternalId ?? string.Empty,
                    Currency = sap.Currency ?? string.Empty,
                    Amount = sap.TotalAmount,
                    Assignament = string.Empty,
                    DateContab = docDateStr,
                    Condition = string.Empty,
                    DateAssign = docDateStr,
                    DaysArrears = "0",
                    Society = string.Empty,
                    RefInvoice = sap.InvoiceNumber ?? string.Empty,
                    TextHead = string.Empty,
                    DateBase = docDateStr,
                    ClassDocument = "DS",
                    Month = sap.Date.Month.ToString("D2"),
                    DocBsad = "00000000",
                    Compensation = string.Empty,
                    YearComp = "0000",
                    Account = string.Empty,
                    Cebe = string.Empty,
                    ConditionDays = "0",
                    ConditionDays2 = "0",
                    ConditionDays3 = "0",
                    Reference2 = string.Empty,
                    Username = "EXT_HANSA",
                    An = string.Empty,
                    AnulContab = string.Empty,
                    YearInvoice = sap.Date.Year.ToString(),
                    DateInvoice = docDateStr,
                    HoursInvoice = DateTime.UtcNow.ToString("HHmmss")
                }
            }
        };

        return new HansaCrmPayloadWrapper
        {
            Object = "hansacrm_hbm_receivable_integrator",
            Entry = new HansaCrmEntry
            {
                Id = Guid.NewGuid().ToString(),
                Date = now,
                Metadata = CreateMetadata(1, 1, 1, 1),
                Client = new HansaCrmCliente { Profile = config.Organization ?? string.Empty, HcrmId = string.Empty },
                Messages = new List<object> { message }
            }
        };
    }

    private static List<HansaCrmAddress> MapAddresses(List<CrmCustomerAddress> addresses)
    {
        if (addresses == null || addresses.Count == 0)
            return new List<HansaCrmAddress>();

        return addresses.Select(a => new HansaCrmAddress
        {
            Type = a.AddressType?.ToLowerInvariant() switch
            {
                "billto" => "billing",
                "shipto" => "shipping",
                _ => a.AddressType ?? "billing"
            },
            City = a.City ?? string.Empty,
            Region = a.State ?? string.Empty,
            Zone = a.County ?? string.Empty,
            Street = a.Street ?? string.Empty,
            Building = a.Block ?? string.Empty,
            Roomnumber = a.BuildingFloorRoom ?? string.Empty,
            Latitude = string.IsNullOrEmpty(a.Latitude) ? null : decimal.TryParse(a.Latitude, out var lat) ? lat : null,
            Longitude = string.IsNullOrEmpty(a.Longitude) ? null : decimal.TryParse(a.Longitude, out var lon) ? lon : null
        }).ToList();
    }

    private static List<HansaCrmBillingInfo> MapBillingInfo(CrmCustomerPayload sap)
    {
        if (string.IsNullOrWhiteSpace(sap.TaxId) && string.IsNullOrWhiteSpace(sap.VatIdNum))
            return new List<HansaCrmBillingInfo>();

        return new List<HansaCrmBillingInfo>
        {
            new()
            {
                BillingName = sap.Name ?? string.Empty,
                DocType = "NIT",
                BillingNro = sap.TaxId ?? sap.VatIdNum ?? string.Empty,
                TaxSystem = "Regimen General"
            }
        };
    }

    private static List<HansaCrmSalesArea> MapSalesArea(CrmCustomerPayload sap, HansaCrmDefaults config)
    {
        // If no defaults configured, return empty list.
        if (string.IsNullOrWhiteSpace(config.Organization))
            return new List<HansaCrmSalesArea>();

        var sector = config.SalesSectors?.FirstOrDefault() ?? string.Empty;
        var channel = config.SalesChannels?.FirstOrDefault() ?? string.Empty;
        var office = config.SalesOffices?.FirstOrDefault() ?? string.Empty;

        return new List<HansaCrmSalesArea>
        {
            new()
            {
                Organization = config.Organization,
                SalesSector = sector,
                SalesChannel = channel,
                SalesOffice = office,
                SalesZone = string.Empty,
                AccountGroup = "1A",
                AccountCategory = "A",
                ShippingCond = string.Empty,
                SupplyCenter = string.Empty,
                TransZone = string.Empty,
                PaymentTerm = string.Empty,
                CreditLimit = sap.CreditLimit,
                Currency = sap.Currency ?? "USD",
                PriceGroup = "02",
                PriceList = string.Empty,
                InventTaking = "1",
                PartnerFunctions = new List<HansaCrmPartnerFunction>
                {
                    new()
                    {
                        Function = "AG",
                        Counter = 0,
                        Code1 = sap.ExternalId ?? string.Empty,
                        PersonalNum = 0,
                        Contact = string.Empty,
                        PartnerCliente = string.Empty
                    }
                }
            }
        };
    }

    public static HansaCrmPayloadWrapper MapAccountBatch(
        List<CrmCustomerPayload> payloads,
        HansaCrmDefaults config,
        int totalRecords,
        int batchRecords,
        int batchQuantity,
        int batchNumber)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var messages = payloads.Select(sap => new HansaCrmAccountMessage
        {
            Code1 = sap.ExternalId ?? string.Empty,
            Name = sap.Name ?? string.Empty,
            Salutation = string.Empty,
            Email1 = sap.Email ?? string.Empty,
            Phone1 = sap.Phone ?? string.Empty,
            Phone2 = sap.Phone2 ?? string.Empty,
            CreditLimit = sap.CreditLimit,
            TransportZone = string.Empty,
            MarketArea = string.Empty,
            DateCreated = sap.UpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? now,
            DateModified = now,
            Address = MapAddresses(sap.Addresses),
            BillingInfo = MapBillingInfo(sap),
            SalesArea = MapSalesArea(sap, config)
        }).Cast<object>().ToList();

        return new HansaCrmPayloadWrapper
        {
            Object = "hansacrm_hbm_account_integrator",
            Entry = new HansaCrmEntry
            {
                Id = Guid.NewGuid().ToString(),
                Date = now,
                Metadata = CreateMetadata(totalRecords, batchRecords, batchQuantity, batchNumber),
                Cliente = new HansaCrmCliente { Profile = config.Organization ?? string.Empty, HcrmId = string.Empty },
                Messages = messages
            }
        };
    }

    public static HansaCrmPayloadWrapper MapVendorBatch(
        List<CrmCustomerPayload> payloads,
        HansaCrmDefaults config,
        int totalRecords,
        int batchRecords,
        int batchQuantity,
        int batchNumber)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var messages = payloads.Select(sap => new HansaCrmVendorMessage
        {
            Code = sap.ExternalId ?? string.Empty,
            Name = sap.Name ?? string.Empty,
            Organization = config.Organization ?? string.Empty,
            SalesSector = (config.SalesSectors ?? new List<string>()).Select(s => new HansaCrmSector { Sector = s }).ToList(),
            SalesChannels = (config.SalesChannels ?? new List<string>()).Select(c => new HansaCrmChannel { Channel = c }).ToList(),
            SalesOffice = (config.SalesOffices ?? new List<string>()).Select(o => new HansaCrmOffice { Office = o }).ToList(),
            Warehouses = (config.Warehouses ?? new List<string>()).Select(w => new HansaCrmWarehouse { Warehouse = w }).ToList(),
            DateCreated = sap.UpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss.fff") ?? now,
            DateModified = now
        }).Cast<object>().ToList();

        return new HansaCrmPayloadWrapper
        {
            Object = "hansacrm_hbm_vendor_integrator",
            Entry = new HansaCrmEntry
            {
                Id = Guid.NewGuid().ToString(),
                Date = now,
                Metadata = CreateMetadata(totalRecords, batchRecords, batchQuantity, batchNumber),
                Cliente = new HansaCrmCliente { Profile = config.Organization ?? string.Empty, HcrmId = string.Empty },
                Messages = messages
            }
        };
    }

    public static HansaCrmPayloadWrapper MapReceivableBatch(
        List<CrmInvoicePayload> payloads,
        HansaCrmDefaults config,
        int totalRecords,
        int batchRecords,
        int batchQuantity,
        int batchNumber)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var messages = payloads.Select(sap => new HansaCrmReceivableMessage
        {
            Code1 = sap.CustomerId ?? string.Empty,
            Receivable = new List<HansaCrmReceivableDetail>
            {
                new()
                {
                    Tcode = "BSID",
                    Code1 = sap.CustomerId ?? string.Empty,
                    DateDocument = sap.Date.ToString("yyyyMMdd"),
                    NroDocument = sap.InvoiceNumber ?? string.Empty,
                    Reference = sap.ExternalId ?? string.Empty,
                    Currency = sap.Currency ?? string.Empty,
                    Amount = sap.TotalAmount,
                    Assignament = string.Empty,
                    DateContab = sap.Date.ToString("yyyyMMdd"),
                    Condition = string.Empty,
                    DateAssign = sap.Date.ToString("yyyyMMdd"),
                    DaysArrears = "0",
                    Society = string.Empty,
                    RefInvoice = sap.InvoiceNumber ?? string.Empty,
                    TextHead = string.Empty,
                    DateBase = sap.Date.ToString("yyyyMMdd"),
                    ClassDocument = "DS",
                    Month = sap.Date.Month.ToString("D2"),
                    DocBsad = "00000000",
                    Compensation = string.Empty,
                    YearComp = "0000",
                    Account = string.Empty,
                    Cebe = string.Empty,
                    ConditionDays = "0",
                    ConditionDays2 = "0",
                    ConditionDays3 = "0",
                    Reference2 = string.Empty,
                    Username = "EXT_HANSA",
                    An = string.Empty,
                    AnulContab = string.Empty,
                    YearInvoice = sap.Date.Year.ToString(),
                    DateInvoice = sap.Date.ToString("yyyyMMdd"),
                    HoursInvoice = DateTime.UtcNow.ToString("HHmmss")
                }
            }
        }).Cast<object>().ToList();

        return new HansaCrmPayloadWrapper
        {
            Object = "hansacrm_hbm_receivable_integrator",
            Entry = new HansaCrmEntry
            {
                Id = Guid.NewGuid().ToString(),
                Date = now,
                Metadata = CreateMetadata(totalRecords, batchRecords, batchQuantity, batchNumber),
                Client = new HansaCrmCliente { Profile = config.Organization ?? string.Empty, HcrmId = string.Empty },
                Messages = messages
            }
        };
    }

    public static HansaCrmPayloadWrapper MapPriceList(PriceListChangedPayload payload, HansaCrmDefaults config)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");

        var message = new HansaCrmPriceListMessage
        {
            ListNum = payload.ListNum,
            CardCode = payload.CardCode,
            IsFullSync = payload.IsFullSync,
            Items = payload.Items.Select(i => new HansaCrmPriceListItem
            {
                ItemCode = i.ItemCode,
                Price = i.Price,
                Currency = i.Currency,
                Discount = i.Discount,
                ValidFrom = i.Periods?.FirstOrDefault()?.FromDate.ToString("yyyy-MM-dd"),
                ValidTo = i.Periods?.FirstOrDefault()?.ToDate.ToString("yyyy-MM-dd")
            }).ToList()
        };

        return new HansaCrmPayloadWrapper
        {
            Object = "hansacrm_hbm_price_list_integrator",
            Entry = new HansaCrmEntry
            {
                Id = Guid.NewGuid().ToString(),
                Date = now,
                Metadata = CreateMetadata(payload.BatchCount, payload.Items.Count, payload.BatchCount, payload.BatchIndex),
                Cliente = new HansaCrmCliente { Profile = config.Organization ?? string.Empty, HcrmId = string.Empty },
                Messages = new List<object> { message }
            }
        };
    }

    public static HansaCrmPayloadWrapper MapPriceListHeader(PriceListHeaderPayload payload, HansaCrmDefaults config)
    {
        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");

        var message = new HansaCrmPriceListHeaderMessage
        {
            ListNum = payload.ListNum,
            ListName = payload.ListName,
            BaseNum = payload.BaseNum,
            Factor = payload.Factor,
            RoundSys = payload.RoundSys,
            GroupCode = payload.GroupCode,
            IsGrossPrc = payload.IsGrossPrc,
            ValidFrom = payload.ValidFrom?.ToString("yyyy-MM-dd"),
            ValidTo = payload.ValidTo?.ToString("yyyy-MM-dd"),
            PrimCurr = payload.PrimCurr,
            AddCurr1 = payload.AddCurr1,
            AddCurr2 = payload.AddCurr2
        };

        return new HansaCrmPayloadWrapper
        {
            Object = "hansacrm_hbm_price_list_header_integrator",
            Entry = new HansaCrmEntry
            {
                Id = Guid.NewGuid().ToString(),
                Date = now,
                Metadata = CreateMetadata(1, 1, 1, 1),
                Cliente = new HansaCrmCliente { Profile = config.Organization ?? string.Empty, HcrmId = string.Empty },
                Messages = new List<object> { message }
            }
        };
    }

    private static HansaCrmMetadata CreateMetadata(int totalRecords, int batchRecords, int batchQuantity, int batchNumber)
    {
        return new HansaCrmMetadata
        {
            BatchId = Guid.NewGuid().ToString(),
            TotalRecords = totalRecords,
            BatchRecords = batchRecords,
            BatchQuantity = batchQuantity,
            BatchNumber = batchNumber
        };
    }
}
