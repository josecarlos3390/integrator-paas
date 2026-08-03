using FluentAssertions;
using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Integration.Shared.Repositories;
using Integration.Shared.Services;
using Integration.Shared.Tests.Helpers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Integration.Shared.Tests.Services;

public class TenantFeatureServiceTests
{
    private static TenantFeatureService CreateService(IntegrationDbContext context, IMemoryCache cache)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddScoped<TenantFeatureFlagRepository>();
        var provider = services.BuildServiceProvider();
        var scopeFactory = provider.GetRequiredService<IServiceScopeFactory>();

        return new TenantFeatureService(scopeFactory, cache, NullLogger<TenantFeatureService>.Instance);
    }

    [Fact]
    public async Task IsEnabledAsync_WhenFlagDoesNotExist_ReturnsTrue_OptOut()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(context, cache);

        var enabled = await service.IsEnabledAsync("tenant-1", "SalesOrderSync");

        enabled.Should().BeTrue();
    }

    [Fact]
    public async Task IsEnabledAsync_WhenFlagDisabled_ReturnsFalse()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(context, cache);

        var repo = new TenantFeatureFlagRepository(context);
        await repo.SetAsync("tenant-1", "SalesOrderSync", false);

        var enabled = await service.IsEnabledAsync("tenant-1", "SalesOrderSync");

        enabled.Should().BeFalse();
    }

    [Fact]
    public async Task IsEnabledAsync_CachesResult()
    {
        await using var context = DbContextHelper.CreateInMemoryContext();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(context, cache);

        var repo = new TenantFeatureFlagRepository(context);
        await repo.SetAsync("tenant-1", "SalesOrderSync", false);

        var first = await service.IsEnabledAsync("tenant-1", "SalesOrderSync");
        first.Should().BeFalse();

        // Modify directly in DB (bypassing cache)
        await repo.SetAsync("tenant-1", "SalesOrderSync", true);

        var second = await service.IsEnabledAsync("tenant-1", "SalesOrderSync");
        second.Should().BeFalse(); // still cached
    }

    [Theory]
    [InlineData("2", "BusinessPartnerSync")]
    [InlineData("4", "ItemSync")]
    [InlineData("13", "InvoiceSync")]
    [InlineData("17", "SalesOrderSync")]
    [InlineData("99", "99")]
    public void ResolveFeatureKey_ReturnsExpectedMapping(string objectType, string expectedKey)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new TenantFeatureService(
            new Mock<IServiceScopeFactory>().Object,
            cache,
            NullLogger<TenantFeatureService>.Instance);

        var key = service.ResolveFeatureKey(objectType);
        key.Should().Be(expectedKey);
    }
}
