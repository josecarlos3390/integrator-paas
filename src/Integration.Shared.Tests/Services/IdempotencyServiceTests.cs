using FluentAssertions;
using Integration.Shared.Configuration;
using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Integration.Shared.Repositories;
using Integration.Shared.Services;
using Integration.Shared.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Integration.Shared.Tests.Services;

public class IdempotencyServiceTests
{
    private static IdempotencyService CreateService(IntegrationDbContext context)
    {
        var repo = new IdempotencyRepository(context);
        var config = Options.Create(new IdempotencyConfig { Enabled = true, TtlDays = 30 });
        return new IdempotencyService(repo, config, NullLogger<IdempotencyService>.Instance);
    }

    [Fact]
    public async Task TryProcessAsync_WhenNotProcessed_ExecutesFuncAndStoresSuccess()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var service = CreateService(context);
        var executed = false;

        var result = await service.TryProcessAsync("t1", "OrderCreated", "agg-1", async () =>
        {
            executed = true;
            await Task.CompletedTask;
        });

        result.Should().Be(IdempotencyResult.Processed);
        executed.Should().BeTrue();

        var record = context.IdempotencyRecords.FirstOrDefault();
        record.Should().NotBeNull();
        record!.Status.Should().Be(IdempotencyStatus.Success);
    }

    [Fact]
    public async Task TryProcessAsync_WhenAlreadySuccess_ReturnsAlreadyProcessed()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var service = CreateService(context);

        await service.TryProcessAsync("t1", "OrderCreated", "agg-1", () => Task.CompletedTask);
        var executed = false;

        var result = await service.TryProcessAsync("t1", "OrderCreated", "agg-1", async () =>
        {
            executed = true;
            await Task.CompletedTask;
        });

        result.Should().Be(IdempotencyResult.AlreadyProcessed);
        executed.Should().BeFalse();
    }

    [Fact]
    public async Task TryProcessAsync_WhenBusinessError_ThrowsAndDoesNotStore()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var service = CreateService(context);

        var act = async () => await service.TryProcessAsync("t1", "OrderCreated", "agg-1", async () =>
        {
            await Task.CompletedTask;
            throw new Integration.Shared.Exceptions.SapIntegrationException("Business error", isBusinessError: true);
        });

        await act.Should().ThrowAsync<Integration.Shared.Exceptions.SapIntegrationException>();
        context.IdempotencyRecords.Should().BeEmpty();
    }

    [Fact]
    public async Task TryProcessAsync_WhenTransientError_StoresTransientErrorAndReturnsFailed()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var service = CreateService(context);

        var result = await service.TryProcessAsync("t1", "OrderCreated", "agg-1", async () =>
        {
            await Task.CompletedTask;
            throw new InvalidOperationException("Transient");
        });

        result.Should().Be(IdempotencyResult.Failed);

        var record = context.IdempotencyRecords.FirstOrDefault();
        record.Should().NotBeNull();
        record!.Status.Should().Be(IdempotencyStatus.TransientError);
    }

    [Fact]
    public async Task InvalidateAsync_ChangesStatusToTransientError()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var service = CreateService(context);

        await service.TryProcessAsync("t1", "OrderCreated", "agg-1", () => Task.CompletedTask);
        await service.InvalidateAsync("t1", "OrderCreated", "agg-1");

        var record = context.IdempotencyRecords.First();
        record.Status.Should().Be(IdempotencyStatus.TransientError);
    }

    [Fact]
    public async Task CleanupExpiredAsync_DeletesOnlyExpiredRecords()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var service = CreateService(context);

        context.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            EventType = "OrderCreated",
            AggregateId = "agg-1",
            Status = IdempotencyStatus.Success,
            ExpiresAt = DateTime.UtcNow.AddDays(-1)
        });
        context.IdempotencyRecords.Add(new IdempotencyRecord
        {
            Id = Guid.NewGuid(),
            TenantId = "t1",
            EventType = "OrderCreated",
            AggregateId = "agg-2",
            Status = IdempotencyStatus.Success,
            ExpiresAt = DateTime.UtcNow.AddDays(1)
        });
        await context.SaveChangesAsync();

        var deleted = await service.CleanupExpiredAsync();

        deleted.Should().Be(1);
        context.IdempotencyRecords.Count().Should().Be(1);
    }
}
