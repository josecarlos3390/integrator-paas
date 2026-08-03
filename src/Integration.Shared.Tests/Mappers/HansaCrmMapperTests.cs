using FluentAssertions;
using Integration.Shared.Connectors.HansaCrm;
using Integration.Shared.Connectors.HansaCrm.Dtos;
using Integration.Shared.Domain;
using Integration.Shared.Dtos;

namespace Integration.Shared.Tests.Mappers;

public class HansaCrmMapperTests
{
    private static HansaCrmDefaults CreateDefaults() => new()
    {
        Organization = "1100",
        SalesSectors = new List<string> { "01", "02" },
        SalesChannels = new List<string> { "01" },
        SalesOffices = new List<string> { "1101" },
        Warehouses = new List<string> { "LP00" }
    };

    [Fact]
    public void MapAccount_SetsCorrectObjectType()
    {
        var sap = new CrmCustomerPayload { ExternalId = "C001", Name = "Acme" };
        var config = CreateDefaults();

        var result = HansaCrmMapper.MapAccount(sap, config);

        result.Object.Should().Be("hansacrm_hbm_account_integrator");
        result.Entry.Id.Should().NotBeNullOrEmpty();
        result.Entry.Messages.Should().HaveCount(1);
    }

    [Fact]
    public void MapAccount_MapsBasicFields()
    {
        var sap = new CrmCustomerPayload
        {
            ExternalId = "C001",
            Name = "Acme Corp",
            Email = "acme@example.com",
            Phone = "123456",
            Phone2 = "789012",
            CreditLimit = 50000,
            UpdatedAt = new DateTime(2024, 1, 15, 10, 30, 0)
        };
        var config = CreateDefaults();

        var result = HansaCrmMapper.MapAccount(sap, config);
        var message = result.Entry.Messages[0] as HansaCrmAccountMessage;

        message.Should().NotBeNull();
        message!.Code1.Should().Be("C001");
        message.Name.Should().Be("Acme Corp");
        message.Email1.Should().Be("acme@example.com");
        message.Phone1.Should().Be("123456");
        message.Phone2.Should().Be("789012");
        message.CreditLimit.Should().Be(50000);
        message.DateCreated.Should().Be("2024-01-15 10:30:00.000");
    }

    [Fact]
    public void MapAccount_MapsAddresses()
    {
        var sap = new CrmCustomerPayload
        {
            ExternalId = "C002",
            Name = "Acme",
            Addresses =
            [
                new CrmCustomerAddress
                {
                    AddressType = "billto",
                    Street = "Main St",
                    City = "La Paz",
                    State = "La Paz",
                    County = "CENTRAL",
                    Block = "Edif A",
                    BuildingFloorRoom = "401",
                    Latitude = "-16.5",
                    Longitude = "-68.1"
                }
            ]
        };
        var config = CreateDefaults();

        var result = HansaCrmMapper.MapAccount(sap, config);
        var message = result.Entry.Messages[0] as HansaCrmAccountMessage;

        message!.Address.Should().HaveCount(1);
        message.Address[0].Type.Should().Be("billing");
        message.Address[0].Street.Should().Be("Main St");
        message.Address[0].City.Should().Be("La Paz");
        message.Address[0].Region.Should().Be("La Paz");
        message.Address[0].Zone.Should().Be("CENTRAL");
        message.Address[0].Building.Should().Be("Edif A");
        message.Address[0].Roomnumber.Should().Be("401");
        message.Address[0].Latitude.Should().Be(-16.5m);
        message.Address[0].Longitude.Should().Be(-68.1m);
    }

    [Fact]
    public void MapAccount_MapsBillingInfo()
    {
        var sap = new CrmCustomerPayload
        {
            ExternalId = "C003",
            Name = "Acme",
            TaxId = "123456789"
        };
        var config = CreateDefaults();

        var result = HansaCrmMapper.MapAccount(sap, config);
        var message = result.Entry.Messages[0] as HansaCrmAccountMessage;

        message!.BillingInfo.Should().HaveCount(1);
        message.BillingInfo[0].BillingName.Should().Be("Acme");
        message.BillingInfo[0].DocType.Should().Be("NIT");
        message.BillingInfo[0].BillingNro.Should().Be("123456789");
        message.BillingInfo[0].TaxSystem.Should().Be("Regimen General");
    }

    [Fact]
    public void MapAccount_WithDefaults_CreatesSalesArea()
    {
        var sap = new CrmCustomerPayload
        {
            ExternalId = "C004",
            Name = "Acme",
            Currency = "BOB"
        };
        var config = CreateDefaults();

        var result = HansaCrmMapper.MapAccount(sap, config);
        var message = result.Entry.Messages[0] as HansaCrmAccountMessage;

        message!.SalesArea.Should().HaveCount(1);
        message.SalesArea[0].Organization.Should().Be("1100");
        message.SalesArea[0].SalesSector.Should().Be("01");
        message.SalesArea[0].SalesChannel.Should().Be("01");
        message.SalesArea[0].SalesOffice.Should().Be("1101");
        message.SalesArea[0].Currency.Should().Be("BOB");
        message.SalesArea[0].PartnerFunctions.Should().HaveCount(1);
        message.SalesArea[0].PartnerFunctions[0].Code1.Should().Be("C004");
    }

    [Fact]
    public void MapAccount_WithoutDefaults_EmptySalesArea()
    {
        var sap = new CrmCustomerPayload { ExternalId = "C005", Name = "Acme" };
        var config = new HansaCrmDefaults();

        var result = HansaCrmMapper.MapAccount(sap, config);
        var message = result.Entry.Messages[0] as HansaCrmAccountMessage;

        message!.SalesArea.Should().BeEmpty();
    }

    [Fact]
    public void MapVendor_SetsCorrectObjectType()
    {
        var sap = new CrmCustomerPayload { ExternalId = "V001", Name = "Supplier" };
        var config = CreateDefaults();

        var result = HansaCrmMapper.MapVendor(sap, config);

        result.Object.Should().Be("hansacrm_hbm_vendor_integrator");
    }

    [Fact]
    public void MapVendor_MapsBasicFields()
    {
        var sap = new CrmCustomerPayload
        {
            ExternalId = "V001",
            Name = "Supplier Inc",
            UpdatedAt = new DateTime(2024, 6, 1, 8, 0, 0)
        };
        var config = CreateDefaults();

        var result = HansaCrmMapper.MapVendor(sap, config);
        var message = result.Entry.Messages[0] as HansaCrmVendorMessage;

        message.Should().NotBeNull();
        message!.Code.Should().Be("V001");
        message.Name.Should().Be("Supplier Inc");
        message.Organization.Should().Be("1100");
        message.SalesSector.Should().HaveCount(2);
        message.SalesChannels.Should().HaveCount(1);
        message.SalesOffice.Should().HaveCount(1);
        message.Warehouses.Should().HaveCount(1);
        message.DateCreated.Should().Be("2024-06-01 08:00:00.000");
    }

    [Fact]
    public void MapReceivable_SetsCorrectObjectType()
    {
        var sap = new CrmInvoicePayload
        {
            ExternalId = "100",
            InvoiceNumber = "INV001",
            CustomerId = "C001",
            Date = new DateTime(2024, 3, 15),
            TotalAmount = 1234.56m,
            Currency = "BOB"
        };
        var config = CreateDefaults();

        var result = HansaCrmMapper.MapReceivable(sap, config);

        result.Object.Should().Be("hansacrm_hbm_receivable_integrator");
        result.Entry.Client.Should().NotBeNull();
    }

    [Fact]
    public void MapReceivable_MapsInvoiceFields()
    {
        var sap = new CrmInvoicePayload
        {
            ExternalId = "100",
            InvoiceNumber = "INV001",
            CustomerId = "C001",
            Date = new DateTime(2024, 3, 15),
            TotalAmount = 1234.56m,
            Currency = "BOB"
        };
        var config = CreateDefaults();

        var result = HansaCrmMapper.MapReceivable(sap, config);
        var message = result.Entry.Messages[0] as HansaCrmReceivableMessage;

        message.Should().NotBeNull();
        message!.Code1.Should().Be("C001");
        message.Receivable.Should().HaveCount(1);
        message.Receivable[0].NroDocument.Should().Be("INV001");
        message.Receivable[0].Reference.Should().Be("100");
        message.Receivable[0].Currency.Should().Be("BOB");
        message.Receivable[0].Amount.Should().Be(1234.56m);
        message.Receivable[0].DateDocument.Should().Be("20240315");
        message.Receivable[0].Month.Should().Be("03");
        message.Receivable[0].YearInvoice.Should().Be("2024");
    }

    [Fact]
    public void MapPriceList_SetsCorrectObjectType()
    {
        var payload = new PriceListChangedPayload
        {
            ListNum = 1,
            CardCode = "C001",
            IsFullSync = true,
            BatchIndex = 1,
            BatchCount = 1,
            Items =
            [
                new PriceListItem { ItemCode = "ITEM-A", Price = 100.00m, Currency = "USD", Discount = 5.0m }
            ]
        };
        var config = CreateDefaults();

        var result = HansaCrmMapper.MapPriceList(payload, config);

        result.Object.Should().Be("hansacrm_hbm_price_list_integrator");
        result.Entry.Messages.Should().HaveCount(1);
    }

    [Fact]
    public void MapPriceList_MapsItemsCorrectly()
    {
        var payload = new PriceListChangedPayload
        {
            ListNum = 2,
            CardCode = "C002",
            IsFullSync = false,
            BatchIndex = 1,
            BatchCount = 2,
            Items =
            [
                new PriceListItem
                {
                    ItemCode = "ITEM-A",
                    Price = 150.00m,
                    Currency = "BOB",
                    Discount = 10.0m,
                    Periods =
                    [
                        new Spp1Period { FromDate = new DateTime(2024, 1, 1), ToDate = new DateTime(2024, 12, 31) }
                    ]
                }
            ]
        };
        var config = CreateDefaults();

        var result = HansaCrmMapper.MapPriceList(payload, config);
        var message = result.Entry.Messages[0] as HansaCrmPriceListMessage;

        message.Should().NotBeNull();
        message!.ListNum.Should().Be(2);
        message.CardCode.Should().Be("C002");
        message.IsFullSync.Should().BeFalse();
        message.Items.Should().HaveCount(1);
        message.Items[0].ItemCode.Should().Be("ITEM-A");
        message.Items[0].Price.Should().Be(150.00m);
        message.Items[0].Currency.Should().Be("BOB");
        message.Items[0].Discount.Should().Be(10.0m);
        message.Items[0].ValidFrom.Should().Be("2024-01-01");
        message.Items[0].ValidTo.Should().Be("2024-12-31");
    }

    [Fact]
    public void MapPriceList_WithoutPeriods_LeavesValidDatesNull()
    {
        var payload = new PriceListChangedPayload
        {
            ListNum = 1,
            Items =
            [
                new PriceListItem { ItemCode = "ITEM-X", Price = 50.00m, Currency = "USD", Discount = 0 }
            ]
        };
        var config = CreateDefaults();

        var result = HansaCrmMapper.MapPriceList(payload, config);
        var message = result.Entry.Messages[0] as HansaCrmPriceListMessage;

        message!.Items[0].ValidFrom.Should().BeNull();
        message.Items[0].ValidTo.Should().BeNull();
    }

    [Fact]
    public void MapPriceListHeader_SetsCorrectObjectType()
    {
        var payload = new PriceListHeaderPayload
        {
            ListNum = 1,
            ListName = "Standard Price List"
        };
        var config = CreateDefaults();

        var result = HansaCrmMapper.MapPriceListHeader(payload, config);

        result.Object.Should().Be("hansacrm_hbm_price_list_header_integrator");
        result.Entry.Messages.Should().HaveCount(1);
    }

    [Fact]
    public void MapPriceListHeader_MapsBasicFields()
    {
        var payload = new PriceListHeaderPayload
        {
            ListNum = 3,
            ListName = "Wholesale",
            BaseNum = 1,
            Factor = 1.15m,
            RoundSys = "R01",
            GroupCode = 10,
            IsGrossPrc = true,
            ValidFrom = new DateTime(2024, 6, 1),
            ValidTo = new DateTime(2024, 12, 31),
            PrimCurr = "USD",
            AddCurr1 = "BOB",
            AddCurr2 = "EUR"
        };
        var config = CreateDefaults();

        var result = HansaCrmMapper.MapPriceListHeader(payload, config);
        var message = result.Entry.Messages[0] as HansaCrmPriceListHeaderMessage;

        message.Should().NotBeNull();
        message!.ListNum.Should().Be(3);
        message.ListName.Should().Be("Wholesale");
        message.BaseNum.Should().Be(1);
        message.Factor.Should().Be(1.15m);
        message.RoundSys.Should().Be("R01");
        message.GroupCode.Should().Be(10);
        message.IsGrossPrc.Should().BeTrue();
        message.ValidFrom.Should().Be("2024-06-01");
        message.ValidTo.Should().Be("2024-12-31");
        message.PrimCurr.Should().Be("USD");
        message.AddCurr1.Should().Be("BOB");
        message.AddCurr2.Should().Be("EUR");
    }
}
