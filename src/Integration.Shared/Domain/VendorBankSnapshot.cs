namespace Integration.Shared.Domain;

/// <summary>
/// Last known bank account of a vendor (supplier) per tenant.
/// Used to detect bank account changes on BusinessPartners updates
/// and raise anti-fraud alerts (VENDOR_BANK_ALERT flow).
/// </summary>
public class VendorBankSnapshot
{
    public string TenantId { get; set; } = string.Empty;
    public string CardCode { get; set; } = string.Empty;
    public string CardName { get; set; } = string.Empty;
    public string? BankCode { get; set; }
    public string? Branch { get; set; }
    public string? AccountNo { get; set; }
    public string? Iban { get; set; }

    /// <summary>
    /// Canonical signature of the vendor's bank accounts collection (OCRB):
    /// normalized "bank|branch|account|iban" rows, sorted, joined with ';'.
    /// Detects added/removed/modified accounts, not just the default one.
    /// </summary>
    public string? AccountsSignature { get; set; }

    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Normalized signature of the bank fields. Two snapshots with the
    /// same signature are considered equal (no bank account change).
    /// </summary>
    public static string BuildSignature(string? bankCode, string? branch, string? accountNo, string? iban)
    {
        static string N(string? v) => (v ?? string.Empty).Trim().ToUpperInvariant();
        // "-1" is SAP's "no bank" sentinel for DefaultBankCode
        var bank = N(bankCode);
        if (bank == "-1") bank = string.Empty;
        return $"{bank}|{N(branch)}|{N(accountNo)}|{N(iban)}";
    }

    /// <summary>
    /// Builds the canonical signature of a bank accounts collection
    /// (rows normalized like BuildSignature, empty rows dropped, sorted).
    /// </summary>
    public static string BuildAccountsSignature(IEnumerable<Dtos.SapBPBankAccount> accounts)
    {
        var rows = accounts
            .Select(a => BuildSignature(a.BankCode, a.Branch, a.AccountNo, a.IBAN))
            .Where(s => s != "|||")
            .OrderBy(s => s, StringComparer.Ordinal);
        return string.Join(";", rows);
    }

    /// <summary>Full signature: header default bank fields + accounts collection.</summary>
    public string Signature => BuildSignature(BankCode, Branch, AccountNo, Iban) + "//" + (AccountsSignature ?? string.Empty);
}
