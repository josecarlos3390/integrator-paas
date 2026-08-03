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
}
