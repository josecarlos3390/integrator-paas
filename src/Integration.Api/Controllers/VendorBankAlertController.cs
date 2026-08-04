using Integration.Shared.Clients;
using Integration.Shared.Domain;
using Integration.Shared.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Integration.Api.Controllers;

/// <summary>
/// Vendor bank account watch (VENDOR_BANK_ALERT flow): baseline management.
/// The baseline is the snapshot each vendor's bank account is compared against
/// to detect changes and raise anti-fraud alerts.
/// </summary>
[ApiController]
[Route("api/admin/vendor-bank")]
public class VendorBankAlertController : ControllerBase
{
    private readonly ITenantClientFactory _clientFactory;
    private readonly VendorBankSnapshotRepository _snapshotRepo;
    private readonly ILogger<VendorBankAlertController> _logger;

    public VendorBankAlertController(
        ITenantClientFactory clientFactory,
        VendorBankSnapshotRepository snapshotRepo,
        ILogger<VendorBankAlertController> logger)
    {
        _clientFactory = clientFactory;
        _snapshotRepo = snapshotRepo;
        _logger = logger;
    }

    /// <summary>
    /// Backfills the baseline with every supplier's current bank account in SAP.
    /// By default only vendors without a snapshot are inserted (first-time load);
    /// pass overwrite=true to reset all baselines to the current SAP values.
    /// Admin routes are not tenant-authenticated, so the tenant is explicit.
    /// </summary>
    [HttpPost("baseline")]
    public async Task<IActionResult> BackfillBaseline([FromQuery] string tenantId, [FromQuery] bool overwrite = false, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return BadRequest(new { Message = "tenantId query parameter is required" });
        var sapClient = await _clientFactory.GetSapClientAsync(tenantId);

        var inserted = 0;
        var skipped = 0;
        var skip = 0;

        while (true)
        {
            var (items, hasMore) = await sapClient.GetVendorBankInfoPageAsync(skip, ct);
            if (items.Count == 0) break;

            foreach (var bp in items)
            {
                if (!overwrite && await _snapshotRepo.GetAsync(tenantId, bp.CardCode, ct) is not null)
                {
                    skipped++;
                    continue;
                }

                await _snapshotRepo.UpsertAsync(new VendorBankSnapshot
                {
                    TenantId = tenantId,
                    CardCode = bp.CardCode,
                    CardName = bp.CardName,
                    BankCode = bp.DefaultBankCode,
                    Branch = bp.DefaultBranch,
                    AccountNo = bp.DefaultAccount,
                    Iban = bp.IBAN,
                    AccountsSignature = VendorBankSnapshot.BuildAccountsSignature(bp.BPBankAccounts),
                    UpdatedAt = DateTime.UtcNow
                }, ct);
                inserted++;
            }

            if (!hasMore) break;
            skip += items.Count;
        }

        _logger.LogInformation("Vendor bank baseline backfill for tenant {TenantId}: {Inserted} upserted, {Skipped} skipped (overwrite={Overwrite})",
            tenantId, inserted, skipped, overwrite);

        return Ok(new { TenantId = tenantId, Upserted = inserted, Skipped = skipped, Overwrite = overwrite });
    }
}
