using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Integration.Shared.Repositories;

/// <summary>
/// Persistence of vendor bank account snapshots (VENDOR_BANK_ALERT flow).
/// Follows the same upsert pattern as PriceSnapshotRepository.
/// </summary>
public class VendorBankSnapshotRepository
{
    private readonly IntegrationDbContext _dbContext;

    public VendorBankSnapshotRepository(IntegrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<VendorBankSnapshot?> GetAsync(string tenantId, string cardCode, CancellationToken ct = default)
    {
        return await _dbContext.VendorBankSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.TenantId == tenantId && v.CardCode == cardCode, ct);
    }

    public async Task UpsertAsync(VendorBankSnapshot snapshot, CancellationToken ct = default)
    {
        var existing = await _dbContext.VendorBankSnapshots
            .FirstOrDefaultAsync(v => v.TenantId == snapshot.TenantId && v.CardCode == snapshot.CardCode, ct);

        if (existing != null)
        {
            existing.CardName = snapshot.CardName;
            existing.BankCode = snapshot.BankCode;
            existing.Branch = snapshot.Branch;
            existing.AccountNo = snapshot.AccountNo;
            existing.Iban = snapshot.Iban;
            existing.AccountsSignature = snapshot.AccountsSignature;
            existing.UpdatedAt = snapshot.UpdatedAt;
        }
        else
        {
            _dbContext.VendorBankSnapshots.Add(snapshot);
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpsertBatchAsync(IEnumerable<VendorBankSnapshot> snapshots, CancellationToken ct = default)
    {
        foreach (var snapshot in snapshots)
        {
            var existing = await _dbContext.VendorBankSnapshots
                .FirstOrDefaultAsync(v => v.TenantId == snapshot.TenantId && v.CardCode == snapshot.CardCode, ct);

            if (existing != null)
            {
                existing.CardName = snapshot.CardName;
                existing.BankCode = snapshot.BankCode;
                existing.Branch = snapshot.Branch;
                existing.AccountNo = snapshot.AccountNo;
                existing.Iban = snapshot.Iban;
                existing.AccountsSignature = snapshot.AccountsSignature;
                existing.UpdatedAt = snapshot.UpdatedAt;
            }
            else
            {
                _dbContext.VendorBankSnapshots.Add(snapshot);
            }
        }

        await _dbContext.SaveChangesAsync(ct);
    }
}
