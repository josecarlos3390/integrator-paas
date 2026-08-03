using FluentAssertions;
using Integration.Shared.Dtos;
using Integration.Shared.Mappers;

namespace Integration.Shared.Tests.Mappers;

public class CustomerMapperTests
{
    [Fact]
    public void ToCrmPayload_MapsBasicFields()
    {
        var sap = new SapBusinessPartner
        {
            CardCode = "C001",
            CardName = "Acme Corp",
            CardForeignName = "Acme",
            CardType = "cCustomer",
            AliasName = "ACME",
            GroupCode = 100,
            Series = 1,
            EmailAddress = "acme@example.com",
            Phone1 = "123456789",
            FederalTaxID = "12345678901",
            Country = "US",
            City = "New York",
            Valid = "tYES",
            Frozen = "tNO",
            UpdateDate = new DateTime(2024, 1, 15),
            UpdateTime = "14:30:00",
            U_ORIGIN = "web",
            U_B_dni_type = "DNI",
            U_Ajuste = "none",
            U_MONEDA = "USD"
        };

        var crm = CustomerMapper.ToCrmPayload(sap);

        crm.ExternalId.Should().Be("C001");
        crm.Name.Should().Be("Acme Corp");
        crm.Type.Should().Be("cCustomer");
        crm.Email.Should().Be("acme@example.com");
        crm.TaxId.Should().Be("12345678901");
        crm.Country.Should().Be("US");
        crm.City.Should().Be("New York");
        crm.Status.Should().Be("active");
        crm.IsActive.Should().BeTrue();
        crm.IsFrozen.Should().BeFalse();
        crm.UdfOrigin.Should().Be("web");
        crm.UpdatedAt.Should().Be(new DateTime(2024, 1, 15, 14, 30, 0));
    }

    [Fact]
    public void ToCrmPayload_WhenFrozen_ReturnsFrozenStatus()
    {
        var sap = new SapBusinessPartner
        {
            CardCode = "C002",
            CardName = "Frozen Corp",
            Valid = "tYES",
            Frozen = "tYES"
        };

        var crm = CustomerMapper.ToCrmPayload(sap);

        crm.Status.Should().Be("frozen");
        crm.IsFrozen.Should().BeTrue();
    }

    [Fact]
    public void ToCrmPayload_WhenInvalid_ReturnsInactiveStatus()
    {
        var sap = new SapBusinessPartner
        {
            CardCode = "C003",
            CardName = "Inactive Corp",
            Valid = "tNO",
            Frozen = "tNO"
        };

        var crm = CustomerMapper.ToCrmPayload(sap);

        crm.Status.Should().Be("inactive");
        crm.IsActive.Should().BeFalse();
    }

    [Fact]
    public void ToCrmPayload_FallsBackToFirstAddressWhenMainAddressEmpty()
    {
        var sap = new SapBusinessPartner
        {
            CardCode = "C004",
            CardName = "No Main Address",
            Address = null,
            Country = null,
            City = null,
            BPAddresses =
            [
                new SapBusinessPartnerAddress
                {
                    AddressName = "Main",
                    Street = "123 Main St",
                    City = "Boston",
                    Country = "US",
                    State = "MA",
                    ZipCode = "02101"
                }
            ]
        };

        var crm = CustomerMapper.ToCrmPayload(sap);

        crm.AddressLine.Should().Be("123 Main St");
        crm.City.Should().Be("Boston");
        crm.Country.Should().Be("US");
        crm.State.Should().Be("MA");
        crm.ZipCode.Should().Be("02101");
    }

    [Fact]
    public void ToCrmPayload_MapsAddressesAndContacts()
    {
        var sap = new SapBusinessPartner
        {
            CardCode = "C005",
            CardName = "With Contacts",
            BPAddresses =
            [
                new SapBusinessPartnerAddress
                {
                    AddressName = "Billing",
                    Street = "Bill St",
                    AddressType = "bo_BillTo",
                    U_LATITUDE = "40.0",
                    U_LONGITUDE = "-74.0"
                },
                new SapBusinessPartnerAddress
                {
                    AddressName = "Shipping",
                    Street = "Ship St",
                    AddressType = "bo_ShipTo"
                }
            ],
            ContactEmployees =
            [
                new SapContactEmployee
                {
                    InternalCode = 1,
                    Name = "John Doe",
                    FirstName = "John",
                    LastName = "Doe",
                    E_Mail = "john@example.com",
                    Active = "tYES"
                }
            ]
        };

        var crm = CustomerMapper.ToCrmPayload(sap);

        crm.Addresses.Should().HaveCount(2);
        crm.Addresses[0].AddressType.Should().Be("billing");
        crm.Addresses[0].Latitude.Should().Be("40.0");
        crm.Addresses[1].AddressType.Should().Be("shipping");

        crm.Contacts.Should().HaveCount(1);
        crm.Contacts[0].Name.Should().Be("John Doe");
        crm.Contacts[0].IsActive.Should().BeTrue();
    }
}
