using System.Collections.Concurrent;
using Integration.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integration.Shared.Infrastructure;

/// <summary>
/// Registry of HANA connection pools, one per configured server.
/// The default (legacy) connection string is always included as "default";
/// additional servers come from Hana:Connections in configuration.
/// Pools are created lazily and reused for the lifetime of the process.
/// </summary>
public sealed class HanaConnectionPoolRegistry : IDisposable
{
    public const string DefaultServerName = "default";

    private readonly IOptions<HanaConfig> _config;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ConcurrentDictionary<string, Lazy<HanaConnectionPool>> _pools = new(StringComparer.OrdinalIgnoreCase);

    public HanaConnectionPoolRegistry(
        IOptions<HanaConfig> config,
        ILoggerFactory loggerFactory)
    {
        _config = config;
        _loggerFactory = loggerFactory;
    }

    /// <summary>
    /// Returns all configured server pools: the default connection first,
    /// then each entry of Hana:Connections. Entries without name or
    /// connection string are skipped.
    /// </summary>
    public IReadOnlyCollection<KeyValuePair<string, HanaConnectionPool>> GetAll()
    {
        var cfg = _config.Value;
        var all = new List<KeyValuePair<string, HanaConnectionPool>>();

        if (!string.IsNullOrWhiteSpace(cfg.ConnectionString))
        {
            all.Add(new(DefaultServerName, GetPool(DefaultServerName, cfg.ConnectionString)));
        }

        foreach (var conn in cfg.Connections)
        {
            if (string.IsNullOrWhiteSpace(conn.Name) || string.IsNullOrWhiteSpace(conn.ConnectionString))
            {
                continue;
            }

            all.Add(new(conn.Name, GetPool(conn.Name, conn.ConnectionString)));
        }

        return all;
    }

    private HanaConnectionPool GetPool(string name, string connectionString)
    {
        return _pools.GetOrAdd(name, _ => new Lazy<HanaConnectionPool>(() =>
            new HanaConnectionPool(connectionString, maxSize: 5, _loggerFactory.CreateLogger<HanaConnectionPool>())))
            .Value;
    }

    public void Dispose()
    {
        foreach (var lazy in _pools.Values)
        {
            if (lazy.IsValueCreated)
            {
                lazy.Value.Dispose();
            }
        }
        _pools.Clear();
    }
}
