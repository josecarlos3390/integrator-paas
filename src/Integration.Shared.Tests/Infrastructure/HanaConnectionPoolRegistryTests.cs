using FluentAssertions;
using Integration.Shared.Configuration;
using Integration.Shared.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Integration.Shared.Tests.Infrastructure;

public class HanaConnectionPoolRegistryTests
{
    private static HanaConnectionPoolRegistry CreateRegistry(HanaConfig config)
    {
        return new HanaConnectionPoolRegistry(Options.Create(config), NullLoggerFactory.Instance);
    }

    [Fact]
    public void GetAll_WithOnlyLegacyConnectionString_ReturnsDefaultPool()
    {
        using var registry = CreateRegistry(new HanaConfig
        {
            ConnectionString = "Driver={HDBODBC};SERVERNODE=host:30015;"
        });

        var all = registry.GetAll();

        all.Should().ContainSingle();
        all.First().Key.Should().Be(HanaConnectionPoolRegistry.DefaultServerName);
    }

    [Fact]
    public void GetAll_WithNamedConnections_ReturnsDefaultPlusNamed()
    {
        using var registry = CreateRegistry(new HanaConfig
        {
            ConnectionString = "Driver={HDBODBC};SERVERNODE=host1:30015;",
            Connections =
            {
                new HanaConnectionConfig { Name = "hanaroda25", ConnectionString = "Driver={HDBODBC};SERVERNODE=host2:30015;" }
            }
        });

        var all = registry.GetAll();

        all.Should().HaveCount(2);
        all.Select(p => p.Key).Should().Equal("default", "hanaroda25");
    }

    [Fact]
    public void GetAll_SkipsEntriesWithoutNameOrConnectionString()
    {
        using var registry = CreateRegistry(new HanaConfig
        {
            ConnectionString = "Driver={HDBODBC};SERVERNODE=host1:30015;",
            Connections =
            {
                new HanaConnectionConfig { Name = "", ConnectionString = "cs" },
                new HanaConnectionConfig { Name = "valid", ConnectionString = "" },
                new HanaConnectionConfig { Name = "valid", ConnectionString = "cs" }
            }
        });

        var all = registry.GetAll();

        all.Should().HaveCount(2);
        all.Select(p => p.Key).Should().Equal("default", "valid");
    }

    [Fact]
    public void GetAll_ReusesSamePoolInstancePerServer()
    {
        using var registry = CreateRegistry(new HanaConfig
        {
            ConnectionString = "Driver={HDBODBC};SERVERNODE=host1:30015;"
        });

        var first = registry.GetAll().First().Value;
        var second = registry.GetAll().First().Value;

        second.Should().BeSameAs(first);
    }
}
