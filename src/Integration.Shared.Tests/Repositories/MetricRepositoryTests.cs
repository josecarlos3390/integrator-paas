using FluentAssertions;
using Integration.Shared.Domain;
using Integration.Shared.Repositories;
using Integration.Shared.Tests.Helpers;

namespace Integration.Shared.Tests.Repositories;

public class MetricRepositoryTests
{
    [Fact]
    public async Task GetTotalEventsProcessedAsync_WithTenantFilter_CountsOnlyThatTenant()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new MetricRepository(context);

        var now = DateTime.UtcNow;
        context.IntegrationLogs.AddRange(
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "tenant-001", Status = "success", CreatedAt = now.AddMinutes(-10) },
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "tenant-001", Status = "error", CreatedAt = now.AddMinutes(-5) },
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "RETAIL", Status = "success", CreatedAt = now.AddMinutes(-3) }
        );
        await context.SaveChangesAsync();

        var count = await repo.GetTotalEventsProcessedAsync(now.AddHours(-1), now, tenantId: "tenant-001");

        count.Should().Be(2);
    }

    [Fact]
    public async Task GetStatusCountsAsync_WithTenantFilter_GroupsOnlyThatTenant()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new MetricRepository(context);

        var now = DateTime.UtcNow;
        context.IntegrationLogs.AddRange(
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "tenant-001", Status = "success", CreatedAt = now },
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "RETAIL", Status = "success", CreatedAt = now },
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "RETAIL", Status = "error", CreatedAt = now }
        );
        await context.SaveChangesAsync();

        var counts = await repo.GetStatusCountsAsync(now.AddHours(-1), now.AddMinutes(1), tenantId: "RETAIL");

        counts.Should().HaveCount(2);
        counts["success"].Should().Be(1);
        counts["error"].Should().Be(1);
    }

    [Fact]
    public async Task GetDeadLetterCountsAsync_WithTenantFilter_GroupsOnlyThatTenant()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new MetricRepository(context);

        var now = DateTime.UtcNow;
        context.DeadLetterEvents.AddRange(
            new DeadLetterEvent { Id = Guid.NewGuid(), TenantId = "tenant-001", EventType = "InvoiceCreated", DeadLetteredAt = now },
            new DeadLetterEvent { Id = Guid.NewGuid(), TenantId = "RETAIL", EventType = "CustomerUpdated", DeadLetteredAt = now }
        );
        await context.SaveChangesAsync();

        var counts = await repo.GetDeadLetterCountsAsync(now.AddHours(-1), now.AddMinutes(1), tenantId: "RETAIL");

        counts.Should().HaveCount(1);
        counts["CustomerUpdated"].Should().Be(1);
    }

    [Fact]
    public async Task GetTotalEventsProcessedAsync_WithoutTenantFilter_CountsAllTenants()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new MetricRepository(context);

        var now = DateTime.UtcNow;
        context.IntegrationLogs.AddRange(
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "tenant-001", Status = "success", CreatedAt = now },
            new IntegrationLog { Id = Guid.NewGuid(), TenantId = "RETAIL", Status = "success", CreatedAt = now }
        );
        await context.SaveChangesAsync();

        var count = await repo.GetTotalEventsProcessedAsync(now.AddHours(-1), now.AddMinutes(1));

        count.Should().Be(2);
    }
}
