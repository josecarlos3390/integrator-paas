using FluentAssertions;
using Integration.Shared.Domain;
using Xunit;

namespace Integration.Shared.Tests.Domain;

public class VendorBankSnapshotTests
{
    [Fact]
    public void BuildSignature_NormalizesCaseAndWhitespace()
    {
        var a = VendorBankSnapshot.BuildSignature("bg_mn ", null, " 1310714788", null);
        var b = VendorBankSnapshot.BuildSignature("BG_MN", null, "1310714788", null);

        a.Should().Be(b);
    }

    [Fact]
    public void BuildSignature_TreatsSapNoBankSentinelAsEmpty()
    {
        var withSentinel = VendorBankSnapshot.BuildSignature("-1", null, null, null);
        var empty = VendorBankSnapshot.BuildSignature(null, null, null, null);

        withSentinel.Should().Be(empty);
    }

    [Fact]
    public void BuildSignature_ChangesWhenAccountChanges()
    {
        var before = VendorBankSnapshot.BuildSignature("BG_MN", null, "1310714788", null);
        var after = VendorBankSnapshot.BuildSignature("BG_MN", null, "999888777", null);

        before.Should().NotBe(after);
    }

    [Fact]
    public void BuildSignature_ChangesWhenBankChangesEvenWithSameAccount()
    {
        var before = VendorBankSnapshot.BuildSignature("BG_MN", null, "1310714788", null);
        var after = VendorBankSnapshot.BuildSignature("BG_PN", null, "1310714788", null);

        before.Should().NotBe(after);
    }

    [Fact]
    public void Signature_UsesInstanceFields()
    {
        var snapshot = new VendorBankSnapshot
        {
            BankCode = "BG_MN",
            Branch = null,
            AccountNo = "1310714788",
            Iban = null
        };

        snapshot.Signature.Should().Be(VendorBankSnapshot.BuildSignature("BG_MN", null, "1310714788", null));
    }
}
