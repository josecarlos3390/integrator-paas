using FluentAssertions;
using Integration.Shared.Domain;
using Integration.Shared.Repositories;
using Integration.Shared.Tests.Helpers;

namespace Integration.Shared.Tests.Repositories;

public class TenantSettingsRepositoryTests
{
    private static TenantSetting CreateSetting(string tenantId, string key, string value) => new()
    {
        TenantId = tenantId,
        SettingKey = key,
        SettingValue = value,
        UpdatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task GetAsync_ReturnsSetting_WhenExists()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantSettingsRepository(context);

        context.TenantSettings.Add(CreateSetting("tenant-001", "smtp.port", "587"));
        await context.SaveChangesAsync();

        var setting = await repo.GetAsync("tenant-001", "smtp.port");

        setting.Should().NotBeNull();
        setting!.SettingValue.Should().Be("587");
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNotExists()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantSettingsRepository(context);

        var setting = await repo.GetAsync("tenant-001", "missing.key");

        setting.Should().BeNull();
    }

    [Fact]
    public async Task GetByTenantAsync_ReturnsOnlyThatTenantSettings()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantSettingsRepository(context);

        context.TenantSettings.Add(CreateSetting("tenant-001", "key.a", "1"));
        context.TenantSettings.Add(CreateSetting("tenant-001", "key.b", "2"));
        context.TenantSettings.Add(CreateSetting("tenant-002", "key.c", "3"));
        await context.SaveChangesAsync();

        var settings = await repo.GetByTenantAsync("tenant-001");

        settings.Should().HaveCount(2);
        settings.Select(s => s.SettingKey).Should().Equal("key.a", "key.b");
    }

    [Fact]
    public async Task SetAsync_CreatesNew_WhenNotExists()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantSettingsRepository(context);

        await repo.SetAsync("tenant-001", "retry.max", "5");

        var setting = await repo.GetAsync("tenant-001", "retry.max");
        setting.Should().NotBeNull();
        setting!.SettingValue.Should().Be("5");
    }

    [Fact]
    public async Task SetAsync_UpdatesExisting_WhenExists()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantSettingsRepository(context);

        await repo.SetAsync("tenant-001", "retry.max", "5");
        await repo.SetAsync("tenant-001", "retry.max", "10");

        var settings = await repo.GetByTenantAsync("tenant-001");
        settings.Should().HaveCount(1);
        settings[0].SettingValue.Should().Be("10");
    }

    [Fact]
    public async Task DeleteAsync_RemovesSetting()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantSettingsRepository(context);

        await repo.SetAsync("tenant-001", "retry.max", "5");
        await repo.DeleteAsync("tenant-001", "retry.max");

        var setting = await repo.GetAsync("tenant-001", "retry.max");
        setting.Should().BeNull();
    }
}
