using FluentAssertions;
using Integration.Shared.Domain;
using Integration.Shared.Repositories;
using Integration.Shared.Tests.Helpers;

namespace Integration.Shared.Tests.Repositories;

public class IntegrationRequestRepositoryTests
{
    [Fact]
    public async Task CreateAsync_PersistsRequest()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new IntegrationRequestRepository(context);

        var request = new IntegrationRequest
        {
            Id = Guid.NewGuid(),
            TenantId = "tenant-001",
            CorrelationId = "corr-123",
            SourceSystem = "SAP",
            TargetSystem = "CRM",
            EntityType = "account",
            Operation = "create",
            ExternalId = "C00001",
            Payload = "{}",
            Status = "received",
            ReceivedAt = DateTime.UtcNow
        };

        await repo.CreateAsync(request);

        var found = await repo.GetByIdAsync(request.Id);
        found.Should().NotBeNull();
        found!.TenantId.Should().Be("tenant-001");
        found.Status.Should().Be("received");
    }

    [Fact]
    public async Task FetchPendingAsync_ReturnsOnlyEligibleRequests()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new IntegrationRequestRepository(context);

        var now = DateTime.UtcNow;
        context.IntegrationRequests.AddRange(
            new IntegrationRequest
            {
                Id = Guid.NewGuid(),
                TenantId = "t1",
                SourceSystem = "SAP",
                TargetSystem = "CRM",
                EntityType = "account",
                Status = "received",
                ReceivedAt = now.AddMinutes(-5)
            },
            new IntegrationRequest
            {
                Id = Guid.NewGuid(),
                TenantId = "t1",
                SourceSystem = "SAP",
                TargetSystem = "CRM",
                EntityType = "invoice",
                Status = "processing",
                ReceivedAt = now.AddMinutes(-4),
                LeasedUntil = now.AddMinutes(5)
            },
            new IntegrationRequest
            {
                Id = Guid.NewGuid(),
                TenantId = "t1",
                SourceSystem = "SAP",
                TargetSystem = "CRM",
                EntityType = "order",
                Status = "failed",
                AttemptCount = 1,
                NextRetryAt = now.AddMinutes(-1),
                ReceivedAt = now.AddMinutes(-10)
            },
            new IntegrationRequest
            {
                Id = Guid.NewGuid(),
                TenantId = "t1",
                SourceSystem = "SAP",
                TargetSystem = "CRM",
                EntityType = "product",
                Status = "failed",
                AttemptCount = 1,
                NextRetryAt = now.AddMinutes(5),
                ReceivedAt = now.AddMinutes(-10)
            }
        );
        await context.SaveChangesAsync();

        var pending = await repo.FetchPendingAsync(10);

        pending.Should().HaveCount(2);
        pending.Select(r => r.EntityType).Should().Contain(["account", "order"]);
    }

    [Fact]
    public async Task AcquireLeaseAsync_OnlyLeasesUnleasedRows()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new IntegrationRequestRepository(context);

        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var now = DateTime.UtcNow;

        context.IntegrationRequests.AddRange(
            new IntegrationRequest
            {
                Id = id1,
                TenantId = "t1",
                SourceSystem = "SAP",
                TargetSystem = "CRM",
                EntityType = "account",
                Status = "received",
                ReceivedAt = now.AddMinutes(-5)
            },
            new IntegrationRequest
            {
                Id = id2,
                TenantId = "t1",
                SourceSystem = "SAP",
                TargetSystem = "CRM",
                EntityType = "invoice",
                Status = "received",
                ReceivedAt = now.AddMinutes(-5),
                LeasedUntil = now.AddMinutes(5)
            }
        );
        await context.SaveChangesAsync();

        var leased = await repo.AcquireLeaseAsync([id1, id2], TimeSpan.FromMinutes(10));

        leased.Should().ContainSingle();
        leased.Should().Contain(id1);

        var r1 = await repo.GetByIdAsync(id1);
        r1!.Status.Should().Be("processing");
        r1.LeasedUntil.Should().BeAfter(now);
    }

    [Fact]
    public async Task CompleteAsync_SetsStatusAndProcessedAt()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new IntegrationRequestRepository(context);

        var id = Guid.NewGuid();
        context.IntegrationRequests.Add(new IntegrationRequest
        {
            Id = id,
            TenantId = "t1",
            SourceSystem = "SAP",
            TargetSystem = "CRM",
            EntityType = "account",
            Status = "processing",
            ReceivedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        await repo.CompleteAsync(id, "doc-123");

        var found = await repo.GetByIdAsync(id);
        found!.Status.Should().Be("completed");
        found.ProcessedAt.Should().NotBeNull();
        found.ResultPayload.Should().Be("doc-123");
    }

    [Fact]
    public async Task FailAsync_SetsRetryFields()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new IntegrationRequestRepository(context);

        var id = Guid.NewGuid();
        context.IntegrationRequests.Add(new IntegrationRequest
        {
            Id = id,
            TenantId = "t1",
            SourceSystem = "SAP",
            TargetSystem = "CRM",
            EntityType = "account",
            Status = "processing",
            AttemptCount = 1,
            ReceivedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        await repo.FailAsync(id, "timeout", TimeSpan.FromMinutes(5));

        var found = await repo.GetByIdAsync(id);
        found!.Status.Should().Be("failed");
        found.AttemptCount.Should().Be(2);
        found.ErrorMessage.Should().Be("timeout");
        found.NextRetryAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DeadLetterAsync_SetsDeadLetterStatus()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var repo = new IntegrationRequestRepository(context);

        var id = Guid.NewGuid();
        context.IntegrationRequests.Add(new IntegrationRequest
        {
            Id = id,
            TenantId = "t1",
            SourceSystem = "SAP",
            TargetSystem = "CRM",
            EntityType = "account",
            Status = "failed",
            AttemptCount = 3,
            ReceivedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        await repo.DeadLetterAsync(id, "max attempts exceeded");

        var found = await repo.GetByIdAsync(id);
        found!.Status.Should().Be("dead_letter");
        found.ProcessedAt.Should().NotBeNull();
        found.ErrorMessage.Should().Be("max attempts exceeded");
    }
}
