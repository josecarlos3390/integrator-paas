using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Integration.Shared.Repositories;

/// <summary>
/// Generic per-tenant key-value settings repository.
/// </summary>
public class TenantSettingsRepository
{
    private readonly IntegrationDbContext _dbContext;

    public TenantSettingsRepository(IntegrationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TenantSetting?> GetAsync(string tenantId, string settingKey, CancellationToken ct = default)
    {
        return await _dbContext.TenantSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.SettingKey == settingKey, ct);
    }

    public async Task<IReadOnlyList<TenantSetting>> GetByTenantAsync(string tenantId, CancellationToken ct = default)
    {
        return await _dbContext.TenantSettings
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId)
            .OrderBy(s => s.SettingKey)
            .ToListAsync(ct);
    }

    public async Task SetAsync(string tenantId, string settingKey, string settingValue, CancellationToken ct = default)
    {
        var existing = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.SettingKey == settingKey, ct);

        if (existing is null)
        {
            _dbContext.TenantSettings.Add(new TenantSetting
            {
                TenantId = tenantId,
                SettingKey = settingKey,
                SettingValue = settingValue,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.SettingValue = settingValue;
            existing.UpdatedAt = DateTime.UtcNow;
            _dbContext.TenantSettings.Update(existing);
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(string tenantId, string settingKey, CancellationToken ct = default)
    {
        var existing = await _dbContext.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.SettingKey == settingKey, ct);

        if (existing is null)
            return;

        _dbContext.TenantSettings.Remove(existing);
        await _dbContext.SaveChangesAsync(ct);
    }
}
