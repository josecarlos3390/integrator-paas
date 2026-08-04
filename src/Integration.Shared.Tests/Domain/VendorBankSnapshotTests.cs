using FluentAssertions;
using Integration.Shared.Domain;
using Integration.Shared.Dtos;
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

        // Full signature = header fields + "//" + accounts collection signature (empty when null)
        snapshot.Signature.Should().Be(VendorBankSnapshot.BuildSignature("BG_MN", null, "1310714788", null) + "//");
    }

    [Fact]
    public void BuildAccountsSignature_SortsRowsAndDropsEmpty()
    {
        var accounts = new List<SapBPBankAccount>
        {
            new() { BankCode = null, AccountNo = null }, // empty row, dropped
            new() { BankCode = "BUN_MN", AccountNo = "111" },
            new() { BankCode = "BCP_MN", AccountNo = "222" }
        };

        var sig = VendorBankSnapshot.BuildAccountsSignature(accounts);

        sig.Should().Be("BCP_MN||222|;BUN_MN||111|");
    }

    [Fact]
    public void BuildAccountsSignature_ChangesWhenAccountAdded()
    {
        var before = new List<SapBPBankAccount> { new() { BankCode = "BUN_MN", AccountNo = "111" } };
        var after = new List<SapBPBankAccount>
        {
            new() { BankCode = "BUN_MN", AccountNo = "111" },
            new() { BankCode = "BCP_MN", AccountNo = "222" }
        };

        VendorBankSnapshot.BuildAccountsSignature(before)
            .Should().NotBe(VendorBankSnapshot.BuildAccountsSignature(after));
    }

    [Fact]
    public void BuildAccountsSignature_OrderIndependent()
    {
        var a = new List<SapBPBankAccount>
        {
            new() { BankCode = "BUN_MN", AccountNo = "111" },
            new() { BankCode = "BCP_MN", AccountNo = "222" }
        };
        var b = a.AsEnumerable().Reverse().ToList();

        VendorBankSnapshot.BuildAccountsSignature(a)
            .Should().Be(VendorBankSnapshot.BuildAccountsSignature(b));
    }
}
