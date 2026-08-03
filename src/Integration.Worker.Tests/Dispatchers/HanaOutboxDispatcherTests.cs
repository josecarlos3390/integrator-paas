using FluentAssertions;
using Integration.Shared.Configuration;
using Xunit;
using Integration.Shared.Connectors;
using Integration.Shared.Connectors.HansaCrm;
using Integration.Shared.Domain;
using Integration.Shared.Repositories;
using Integration.Shared.Services;
using Integration.Worker.Dispatchers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Integration.Worker.Tests.Dispatchers;

public class HanaOutboxDispatcherTests
{
    private static readonly IOptions<OutboxConfig> DefaultOutboxOptions = Options.Create(new OutboxConfig());

    private static HanaOutboxDispatcher CreateDispatcher(
        Mock<HanaOutboxRepository>? hanaRepo = null,
        Mock<IntegrationLogRepository>? logRepo = null,
        Mock<TenantQuotaRepository>? quotaRepo = null,
        Mock<ITenantFeatureService>? featureService = null,
        Mock<IIdempotencyService>? idempotencyService = null)
    {
        var hanaRepoMock = hanaRepo ?? new Mock<HanaOutboxRepository>(MockBehavior.Loose, null!, DefaultOutboxOptions, null!);
        var logRepoMock = logRepo ?? new Mock<IntegrationLogRepository>(MockBehavior.Loose, null!);
        var quotaRepoMock = quotaRepo ?? new Mock<TenantQuotaRepository>(MockBehavior.Loose, null!);

        return new HanaOutboxDispatcher(
            hanaRepoMock.Object,
            new Mock<Integration.Shared.Clients.ITenantClientFactory>().Object,
            logRepoMock.Object,
            new Mock<DeadLetterRepository>(MockBehavior.Loose, null!).Object,
            featureService?.Object ?? new Mock<ITenantFeatureService>().Object,
            new Mock<IAlertingService>().Object,
            idempotencyService?.Object ?? new Mock<IIdempotencyService>().Object,
            quotaRepoMock.Object,
            Options.Create(new OutboxConfig { BatchSize = 10, MaxAttempts = 5, PollingSeconds = 5 }),
            Options.Create(new HansaCrmConfig()),
            NullLogger<HanaOutboxDispatcher>.Instance,
            new Mock<IServiceScopeFactory>().Object);
    }

    [Fact]
    public async Task ProcessEventAsync_WhenFeatureDisabled_MarksProcessedAndSkips()
    {
        var hanaRepo = new Mock<HanaOutboxRepository>(MockBehavior.Strict, null!, DefaultOutboxOptions, null!);
        hanaRepo.Setup(x => x.MarkProcessedAsync("evt-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var featureService = new Mock<ITenantFeatureService>();
        featureService.Setup(x => x.ResolveFeatureKey(It.IsAny<string>())).Returns("TestFeature");
        featureService.Setup(x => x.IsEnabledAsync("t1", "TestFeature", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var dispatcher = CreateDispatcher(hanaRepo: hanaRepo, featureService: featureService);
        var evt = new HanaOutboxEvent { Id = "evt-1", TenantId = "t1", ObjectType = "13", EventType = "Created", AggregateId = "1001" };

        await dispatcher.ProcessEventAsync(evt, null, CancellationToken.None);

        hanaRepo.Verify(x => x.MarkProcessedAsync("evt-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ProcessEventAsync_WhenQuotaExceeded_DelaysEventAndSkips()
    {
        var hanaRepo = new Mock<HanaOutboxRepository>(MockBehavior.Strict, null!, DefaultOutboxOptions, null!);
        hanaRepo.Setup(x => x.DelayEventAsync("evt-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var logRepo = new Mock<IntegrationLogRepository>(MockBehavior.Loose, null!);
        logRepo.Setup(x => x.GetProcessedCountByTenantAsync("t1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1500);

        var quotaRepo = new Mock<TenantQuotaRepository>(MockBehavior.Strict, null!);
        quotaRepo.Setup(x => x.GetAsync("t1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantQuota { TenantId = "t1", MaxEventsPerHour = 1000 });

        var featureService = new Mock<ITenantFeatureService>();
        featureService.Setup(x => x.ResolveFeatureKey(It.IsAny<string>())).Returns("TestFeature");
        featureService.Setup(x => x.IsEnabledAsync("t1", "TestFeature", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var dispatcher = CreateDispatcher(
            hanaRepo: hanaRepo,
            logRepo: logRepo,
            quotaRepo: quotaRepo,
            featureService: featureService);

        var evt = new HanaOutboxEvent { Id = "evt-1", TenantId = "t1", ObjectType = "13", EventType = "Created", AggregateId = "1001" };

        await dispatcher.ProcessEventAsync(evt, null, CancellationToken.None);

        hanaRepo.Verify(x => x.DelayEventAsync("evt-1", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
        hanaRepo.Verify(x => x.MarkProcessedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task IsQuotaExceededAsync_WhenUnderLimit_ReturnsFalse()
    {
        var logRepo = new Mock<IntegrationLogRepository>(MockBehavior.Strict, null!);
        logRepo.Setup(x => x.GetProcessedCountByTenantAsync("t1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(500);

        var dispatcher = CreateDispatcher(logRepo: logRepo);
        var quota = new TenantQuota { TenantId = "t1", MaxEventsPerHour = 1000 };

        var result = await dispatcher.IsQuotaExceededAsync("t1", quota, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsQuotaExceededAsync_WhenOverLimit_ReturnsTrue()
    {
        var logRepo = new Mock<IntegrationLogRepository>(MockBehavior.Strict, null!);
        logRepo.Setup(x => x.GetProcessedCountByTenantAsync("t1", It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1200);

        var dispatcher = CreateDispatcher(logRepo: logRepo);
        var quota = new TenantQuota { TenantId = "t1", MaxEventsPerHour = 1000 };

        var result = await dispatcher.IsQuotaExceededAsync("t1", quota, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsQuotaExceededAsync_WhenLimitIsZero_ReturnsFalse()
    {
        var dispatcher = CreateDispatcher();
        var quota = new TenantQuota { TenantId = "t1", MaxEventsPerHour = 0 };

        var result = await dispatcher.IsQuotaExceededAsync("t1", quota, CancellationToken.None);

        result.Should().BeFalse();
    }
}
