using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Integration.Shared.Repositories;

public class PriceSnapshotRepository
{
    private readonly IntegrationDbContext _dbContext;

    public PriceSnapshotRepository(IntegrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PriceSnapshot?> GetAsync(string tenantId, string itemCode, int priceList, CancellationToken ct = default)
    {
        return await _dbContext.PriceSnapshots
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.ItemCode == itemCode && p.PriceList == priceList, ct);
    }

    public async Task UpsertAsync(PriceSnapshot snapshot, CancellationToken ct = default)
    {
        var existing = await _dbContext.PriceSnapshots
            .FirstOrDefaultAsync(p => p.TenantId == snapshot.TenantId && p.ItemCode == snapshot.ItemCode && p.PriceList == snapshot.PriceList, ct);

        if (existing != null)
        {
            existing.Price = snapshot.Price;
            existing.Currency = snapshot.Currency;
            existing.DiscountPercent = snapshot.DiscountPercent;
            existing.PriceHash = snapshot.PriceHash;
            existing.SapUpdateDate = snapshot.SapUpdateDate;
            existing.SapUpdateTs = snapshot.SapUpdateTs;
            existing.LastSyncedAt = snapshot.LastSyncedAt;
        }
        else
        {
            _dbContext.PriceSnapshots.Add(snapshot);
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpsertBatchAsync(IEnumerable<PriceSnapshot> snapshots, CancellationToken ct = default)
    {
        foreach (var snapshot in snapshots)
        {
            var existing = await _dbContext.PriceSnapshots
                .FirstOrDefaultAsync(p => p.TenantId == snapshot.TenantId && p.ItemCode == snapshot.ItemCode && p.PriceList == snapshot.PriceList, ct);

            if (existing != null)
            {
                existing.Price = snapshot.Price;
                existing.Currency = snapshot.Currency;
                existing.DiscountPercent = snapshot.DiscountPercent;
                existing.PriceHash = snapshot.PriceHash;
                existing.SapUpdateDate = snapshot.SapUpdateDate;
                existing.SapUpdateTs = snapshot.SapUpdateTs;
                existing.LastSyncedAt = snapshot.LastSyncedAt;
            }
            else
            {
                _dbContext.PriceSnapshots.Add(snapshot);
            }
        }

        await _dbContext.SaveChangesAsync(ct);
    }
}
