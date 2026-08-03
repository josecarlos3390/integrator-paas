using FluentAssertions;
using Integration.Shared.Dtos;
using Integration.Shared.Mappers;

namespace Integration.Shared.Tests.Mappers;

public class InvoiceMapperTests
{
    [Fact]
    public void ToCrmPayload_MapsBasicFields()
    {
        var sap = new SapInvoice
        {
            DocEntry = 1001,
            DocNum = 2002,
            CardCode = "C001",
            CardName = "Customer One",
            DocDate = "2024-03-15",
            DocTotal = 1500.00m,
            DocCurrency = "USD",
            DocumentLines =
            [
                new SapInvoiceLine { ItemCode = "ITEM-A", ItemDescription = "Desc A", Quantity = 2, UnitPrice = 500, LineTotal = 1000 },
                new SapInvoiceLine { ItemCode = "ITEM-B", ItemDescription = "Desc B", Quantity = 1, UnitPrice = 500, LineTotal = 500 }
            ]
        };

        var crm = InvoiceMapper.ToCrmPayload(sap);

        crm.ExternalId.Should().Be("1001");
        crm.InvoiceNumber.Should().Be("2002");
        crm.CustomerId.Should().Be("C001");
        crm.CustomerName.Should().Be("Customer One");
        crm.Date.Should().Be(new DateTime(2024, 3, 15));
        crm.TotalAmount.Should().Be(1500.00m);
        crm.Currency.Should().Be("USD");
        crm.LineItems.Should().HaveCount(2);

        crm.LineItems[0].Sku.Should().Be("ITEM-A");
        crm.LineItems[0].Total.Should().Be(1000);
    }

    [Fact]
    public void ToCrmPayload_WhenInvalidDate_UsesMinValue()
    {
        var sap = new SapInvoice
        {
            DocEntry = 1,
            DocNum = 1,
            CardCode = "C001",
            DocDate = "invalid-date",
            DocumentLines = []
        };

        var crm = InvoiceMapper.ToCrmPayload(sap);

        crm.Date.Should().Be(DateTime.MinValue);
    }
}
