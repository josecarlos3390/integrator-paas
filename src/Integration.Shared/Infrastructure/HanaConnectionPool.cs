using System.Collections.Concurrent;
using System.Data;
using System.Data.Odbc;
using Microsoft.Extensions.Logging;

namespace Integration.Shared.Infrastructure;

/// <summary>
/// Simple connection pool for SAP HANA ODBC.
/// Keeps a bounded set of warm connections to avoid TCP overhead on every query.
/// Thread-safe via SemaphoreSlim + ConcurrentBag.
/// </summary>
public class HanaConnectionPool : IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<HanaConnectionPool> _logger;
    private readonly ConcurrentBag<OdbcConnection> _pool = new();
    private readonly SemaphoreSlim _semaphore;
    private readonly int _maxSize;
    private int _createdCount;

    public HanaConnectionPool(string connectionString, int maxSize, ILogger<HanaConnectionPool> logger)
    {
        _connectionString = connectionString;
        _maxSize = Math.Max(1, maxSize);
        _semaphore = new SemaphoreSlim(_maxSize);
        _logger = logger;
    }

    private OdbcConnection CreateConnection()
    {
        var conn = new OdbcConnection(_connectionString);
        conn.Open();
        var count = Interlocked.Increment(ref _createdCount);
        _logger.LogDebug("Created new HANA connection (total created: {Count}, pool max: {Max})", count, _maxSize);
        return conn;
    }

    /// <summary>
    /// Acquires a connection from the pool. Must be disposed to return it.
    /// </summary>
    public async Task<PoolLease> AcquireAsync(CancellationToken ct = default)
    {
        await _semaphore.WaitAsync(ct);
        try
        {
            if (_pool.TryTake(out var conn) && conn.State == ConnectionState.Open)
            {
                return new PoolLease(this, conn);
            }
            return new PoolLease(this, CreateConnection());
        }
        catch
        {
            _semaphore.Release();
            throw;
        }
    }

    internal void Release(OdbcConnection conn)
    {
        if (conn.State == ConnectionState.Open)
        {
            _pool.Add(conn);
        }
        else
        {
            try { conn.Dispose(); } catch { /* best effort */ }
        }
        _semaphore.Release();
    }

    public void Dispose()
    {
        while (_pool.TryTake(out var conn))
        {
            try { conn.Close(); } catch { }
            try { conn.Dispose(); } catch { }
        }
        _semaphore.Dispose();
    }
}

/// <summary>
/// Disposable wrapper that returns the connection to the pool on Dispose.
/// </summary>
public readonly struct PoolLease : IDisposable
{
    private readonly HanaConnectionPool? _pool;
    public OdbcConnection Connection { get; }

    public PoolLease(HanaConnectionPool pool, OdbcConnection connection)
    {
        _pool = pool;
        Connection = connection;
    }

    public void Dispose()
    {
        _pool?.Release(Connection);
    }
}
