using FluentAssertions;
using Integration.Shared.Domain;
using Integration.Shared.Repositories;
using Integration.Shared.Tests.Helpers;

namespace Integration.Shared.Tests.Repositories;

public class TenantQuotaRepositoryTests
{
    [Fact]
    public async Task GetAsync_ReturnsQuota_WhenExists()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantQuotaRepository(context);

        context.TenantQuotas.Add(new TenantQuota
        {
            TenantId = "tenant-001",
            MaxEventsPerHour = 500,
            MaxApiCallsPerMinute = 50
        });
        await context.SaveChangesAsync();

        var quota = await repo.GetAsync("tenant-001");

        quota.Should().NotBeNull();
        quota!.MaxEventsPerHour.Should().Be(500);
        quota.MaxApiCallsPerMinute.Should().Be(50);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_WhenNotExists()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantQuotaRepository(context);

        var quota = await repo.GetAsync("tenant-999");

        quota.Should().BeNull();
    }

    [Fact]
    public async Task UpsertAsync_CreatesNew_WhenNotExists()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantQuotaRepository(context);

        await repo.UpsertAsync(new TenantQuota
        {
            TenantId = "tenant-001",
            MaxEventsPerHour = 2000,
            MaxApiCallsPerMinute = 200
        });

        var quota = await repo.GetAsync("tenant-001");
        quota.Should().NotBeNull();
        quota!.MaxEventsPerHour.Should().Be(2000);
    }

    [Fact]
    public async Task UpsertAsync_UpdatesExisting_WhenExists()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new TenantQuotaRepository(context);

        context.TenantQuotas.Add(new TenantQuota
        {
            TenantId = "tenant-001",
            MaxEventsPerHour = 1000,
            MaxApiCallsPerMinute = 100
        });
        await context.SaveChangesAsync();

        await repo.UpsertAsync(new TenantQuota
        {
            TenantId = "tenant-001",
            MaxEventsPerHour = 500,
            MaxApiCallsPerMinute = 50
        });

        var quota = await repo.GetAsync("tenant-001");
        quota.Should().NotBeNull();
        quota!.MaxEventsPerHour.Should().Be(500);
        quota.MaxApiCallsPerMinute.Should().Be(50);
    }
}
