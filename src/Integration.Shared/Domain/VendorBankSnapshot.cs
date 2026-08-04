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

    public string Signature => BuildSignature(BankCode, Branch, AccountNo, Iban);
}
