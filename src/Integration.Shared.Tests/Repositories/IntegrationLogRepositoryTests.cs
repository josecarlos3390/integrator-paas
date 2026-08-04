using FluentAssertions;
using Integration.Shared.Domain;
using Integration.Shared.Repositories;
using Integration.Shared.Tests.Helpers;

namespace Integration.Shared.Tests.Repositories;

public class IntegrationLogRepositoryTests
{
    [Fact]
    public async Task GetProcessedCountByTenantAsync_ReturnsCorrectCount()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new IntegrationLogRepository(context);

        var tenantId = "tenant-001";
        var now = DateTime.UtcNow;

        context.IntegrationLogs.AddRange(
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = tenantId, Status = "success", CreatedAt = now.AddMinutes(-30) },
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = tenantId, Status = "success", CreatedAt = now.AddMinutes(-45) },
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = tenantId, Status = "error", CreatedAt = now.AddMinutes(-10) },
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "tenant-002", Status = "success", CreatedAt = now.AddMinutes(-20) },
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = tenantId, Status = "success", CreatedAt = now.AddHours(-2) }
        );
        await context.SaveChangesAsync();

        var count = await repo.GetProcessedCountByTenantAsync(tenantId, now.AddHours(-1));

        count.Should().Be(3);
    }

    [Fact]
    public async Task GetProcessedCountByTenantAsync_WhenNoLogs_ReturnsZero()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new IntegrationLogRepository(context);

        var count = await repo.GetProcessedCountByTenantAsync("tenant-999", DateTime.UtcNow.AddHours(-1));

        count.Should().Be(0);
    }

    [Fact]
    public async Task GetRecentAsync_WithTenantFilter_ReturnsOnlyThatTenant()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new IntegrationLogRepository(context);

        var now = DateTime.UtcNow;
        context.IntegrationLogs.AddRange(
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "tenant-001", Status = "success", CreatedAt = now.AddMinutes(-5) },
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "tenant-001", Status = "error", CreatedAt = now.AddMinutes(-3) },
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "RETAIL", Status = "success", CreatedAt = now.AddMinutes(-1) }
        );
        await context.SaveChangesAsync();

        var (items, totalCount) = await repo.GetRecentAsync(tenantId: "RETAIL");

        totalCount.Should().Be(1);
        items.Should().OnlyContain(l => l.TenantId == "RETAIL");
    }

    [Fact]
    public async Task GetRecentAsync_WithoutTenantFilter_ReturnsAllTenants()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new IntegrationLogRepository(context);

        var now = DateTime.UtcNow;
        context.IntegrationLogs.AddRange(
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "tenant-001", Status = "success", CreatedAt = now },
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "RETAIL", Status = "success", CreatedAt = now }
        );
        await context.SaveChangesAsync();

        var (_, totalCount) = await repo.GetRecentAsync();

        totalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetRecentAsync_DateRangeWithTenantFilter_ReturnsOnlyThatTenant()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new IntegrationLogRepository(context);

        var now = DateTime.UtcNow;
        context.IntegrationLogs.AddRange(
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "tenant-001", Status = "idempotency_hit", CreatedAt = now.AddMinutes(-10) },
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "RETAIL", Status = "idempotency_hit", CreatedAt = now.AddMinutes(-5) }
        );
        await context.SaveChangesAsync();

        var logs = await repo.GetRecentAsync(now.AddHours(-1), now, tenantId: "tenant-001");

        logs.Should().HaveCount(1);
        logs.Should().OnlyContain(l => l.TenantId == "tenant-001");
    }
}
