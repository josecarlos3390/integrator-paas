using FluentAssertions;
using Integration.Shared.Dtos;
using Integration.Shared.Mappers;

namespace Integration.Shared.Tests.Mappers;

public class OrderMapperTests
{
    [Fact]
    public void ToSapPayload_MapsBasicFields()
    {
        var crm = new CrmOrderPayload
        {
            CrmOrderId = "ORD-123",
            CustomerId = "C001",
            OrderDate = new DateTime(2024, 6, 15),
            DueDate = new DateTime(2024, 6, 20),
            Warehouse = "WH01",
            Lines =
            [
                new CrmOrderLine { Sku = "ITEM-A", Quantity = 2, Price = 10.5m, Warehouse = "WH02" },
                new CrmOrderLine { Sku = "ITEM-B", Quantity = 1, Price = 5.0m }
            ]
        };

        var sap = OrderMapper.ToSapPayload(crm);

        sap.CardCode.Should().Be("C001");
        sap.DocDate.Should().Be("2024-06-15");
        sap.DocDueDate.Should().Be("2024-06-20");
        sap.NumAtCard.Should().Be("ORD-123");
        sap.Comments.Should().Be("CRM Order: ORD-123");
        sap.DocumentLines.Should().HaveCount(2);

        sap.DocumentLines[0].ItemCode.Should().Be("ITEM-A");
        sap.DocumentLines[0].Quantity.Should().Be(2);
        sap.DocumentLines[0].UnitPrice.Should().Be(10.5m);
        sap.DocumentLines[0].WarehouseCode.Should().Be("WH02");

        sap.DocumentLines[1].WarehouseCode.Should().Be("WH01"); // fallback to header warehouse
    }

    [Fact]
    public void ToSapPayload_WhenNoDueDate_LeavesDocDueDateNull()
    {
        var crm = new CrmOrderPayload
        {
            CrmOrderId = "ORD-1",
            CustomerId = "C001",
            OrderDate = new DateTime(2024, 1, 1),
            Lines = []
        };

        var sap = OrderMapper.ToSapPayload(crm);

        sap.DocDueDate.Should().BeNull();
    }

    [Fact]
    public void ToSapPayload_SetsUSyncOriginToCrm()
    {
        var crm = new CrmOrderPayload
        {
            CrmOrderId = "ORD-999",
            CustomerId = "C001",
            OrderDate = new DateTime(2024, 6, 15),
            Lines = []
        };

        var sap = OrderMapper.ToSapPayload(crm);

        sap.USyncOrigin.Should().Be("CRM");
    }
}
