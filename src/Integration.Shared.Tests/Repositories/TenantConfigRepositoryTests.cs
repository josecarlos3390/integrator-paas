using FluentAssertions;
using Integration.Shared.Connectors;
using Integration.Shared.Domain;
using Integration.Shared.Repositories;
using Integration.Shared.Tests.Helpers;

namespace Integration.Shared.Tests.Repositories;

public class TenantConfigRepositoryTests
{
    private static TenantConfig CreateConfig(string tenantId, bool isActive = true) => new()
    {
        TenantId = tenantId,
        Name = $"Tenant {tenantId}",
        ApiKeyHash = "hash-" + tenantId,
        IsActive = isActive,
        CreatedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task GetByIdAsync_ReturnsNull_WhenTenantIsInactive()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantConfigRepository(context);

        context.TenantConfigs.Add(CreateConfig("tenant-001", isActive: false));
        await context.SaveChangesAsync();

        var config = await repo.GetByIdAsync("tenant-001");

        config.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdIncludingInactiveAsync_ReturnsInactiveTenant()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantConfigRepository(context);

        context.TenantConfigs.Add(CreateConfig("tenant-001", isActive: false));
        await context.SaveChangesAsync();

        var config = await repo.GetByIdIncludingInactiveAsync("tenant-001");

        config.Should().NotBeNull();
        config!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task GetAllIncludingInactiveAsync_IncludesInactiveTenants()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantConfigRepository(context);

        context.TenantConfigs.Add(CreateConfig("tenant-active"));
        context.TenantConfigs.Add(CreateConfig("tenant-inactive", isActive: false));
        await context.SaveChangesAsync();

        var all = await repo.GetAllIncludingInactiveAsync();
        var activeOnly = await repo.GetAllAsync();

        all.Should().HaveCount(2);
        activeOnly.Should().HaveCount(1);
        activeOnly[0].TenantId.Should().Be("tenant-active");
    }

    [Fact]
    public async Task CreateAsync_PersistsTenant()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantConfigRepository(context);

        var config = CreateConfig("tenant-001");
        config.SmtpHost = "smtp.gruporoda.com";
        config.EmailEnabled = true;

        await repo.CreateAsync(config);

        var persisted = await repo.GetByIdIncludingInactiveAsync("tenant-001");
        persisted.Should().NotBeNull();
        persisted!.SmtpHost.Should().Be("smtp.gruporoda.com");
        persisted.EmailEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateAsync_PersistsChanges()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantConfigRepository(context);

        await repo.CreateAsync(CreateConfig("tenant-001"));
        var config = await repo.GetByIdIncludingInactiveAsync("tenant-001");
        config!.SapServiceLayerUrl = "https://sap.example.com:50000";
        config.SmtpPort = 465;

        await repo.UpdateAsync(config);

        var updated = await repo.GetByIdIncludingInactiveAsync("tenant-001");
        updated!.SapServiceLayerUrl.Should().Be("https://sap.example.com:50000");
        updated.SmtpPort.Should().Be(465);
    }

    [Fact]
    public async Task SetActiveAsync_TogglesTenantStatus()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantConfigRepository(context);

        await repo.CreateAsync(CreateConfig("tenant-001"));
        await repo.SetActiveAsync("tenant-001", false);

        var config = await repo.GetByIdIncludingInactiveAsync("tenant-001");
        config!.IsActive.Should().BeFalse();
        (await repo.GetByIdAsync("tenant-001")).Should().BeNull();
    }

    [Fact]
    public async Task UpdateApiKeyHashAsync_ChangesHash()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantConfigRepository(context);

        await repo.CreateAsync(CreateConfig("tenant-001"));
        await repo.UpdateApiKeyHashAsync("tenant-001", "new-hash");

        var config = await repo.GetByIdIncludingInactiveAsync("tenant-001");
        config!.ApiKeyHash.Should().Be("new-hash");
    }
}
