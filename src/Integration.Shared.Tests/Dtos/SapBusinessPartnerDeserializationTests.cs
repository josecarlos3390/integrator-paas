using System.Text.Json;
using FluentAssertions;
using Integration.Shared.Dtos;
using Xunit;

namespace Integration.Shared.Tests.Dtos;

public class SapBusinessPartnerDeserializationTests
{
    [Fact]
    public void Deserialize_PopulatesBPBankAccounts()
    {
        // Mirrors what Service Layer returns inside the "value" array of a page query
        var json = """
        {
            "CardCode": "PL001028",
            "CardName": "MINISTERIO",
            "CardType": "cSupplier",
            "DefaultBankCode": "BUN_MN",
            "DefaultAccount": "10000004671306",
            "BPBankAccounts": [
                { "LogInstance": 0, "BPCode": "PL001028", "BankCode": "BUN_MN", "AccountNo": "10000004671306", "IBAN": null }
            ]
        }
        """;

        var bp = JsonSerializer.Deserialize<SapBusinessPartner>(json);

        bp.Should().NotBeNull();
        bp!.CardCode.Should().Be("PL001028");
        bp.DefaultBankCode.Should().Be("BUN_MN");
        bp.BPBankAccounts.Should().HaveCount(1);
        bp.BPBankAccounts[0].AccountNo.Should().Be("10000004671306");
    }
}
