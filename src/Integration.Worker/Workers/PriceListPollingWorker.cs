using System.Data.Odbc;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Integration.Shared.Configuration;
using Integration.Shared.Domain;
using Integration.Shared.Infrastructure;
using Integration.Shared.Repositories;
using Microsoft.Extensions.Options;

namespace Integration.Worker.Workers;

public class PriceListPollingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<PriceListPollingConfig> _config;
    private readonly HanaConnectionPool _hanaPool;
    private readonly ILogger<PriceListPollingWorker> _logger;
    private DateTime? _lastItemPriceSync;

    public PriceListPollingWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<PriceListPollingConfig> config,
        HanaConnectionPool hanaPool,
        ILogger<PriceListPollingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _config = config;
        _hanaPool = hanaPool;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Value.Enabled)
        {
            _logger.LogInformation("PriceListPollingWorker is disabled");
            return;
        }

        _logger.LogInformation("PriceListPollingWorker started. Interval={Minutes}m, GroupBy={GroupBy}",
            _config.Value.IntervalMinutes, _config.Value.GroupBy);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in PriceListPollingWorker cycle");
            }

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(_config.Value.IntervalMinutes), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("PriceListPollingWorker stopping");
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var cursorRepo = scope.ServiceProvider.GetRequiredService<PollingCursorRepository>();
        var snapshotRepo = scope.ServiceProvider.GetRequiredService<PriceSnapshotRepository>();
        var hanaRepo = scope.ServiceProvider.GetRequiredService<HanaOutboxRepository>();

        var tenantId = "tenant-001";

        // === OPLN Incremental Sync (Price List headers) ===
        await SyncOplnAsync(tenantId, cursorRepo, snapshotRepo, hanaRepo, ct);

        // === OSPP Incremental Sync ===
        await SyncOsppAsync(tenantId, cursorRepo, snapshotRepo, hanaRepo, ct);

        // === ITM1 Full Scan (optional, by interval) ===
        if (_config.Value.IncludeItemPrices)
        {
            var hoursSinceLastSync = _lastItemPriceSync.HasValue
                ? (DateTime.UtcNow - _lastItemPriceSync.Value).TotalHours
                : double.MaxValue;

            if (hoursSinceLastSync >= _config.Value.ItemPriceFullSyncIntervalHours)
            {
                await SyncItm1Async(tenantId, snapshotRepo, hanaRepo, ct);
                _lastItemPriceSync = DateTime.UtcNow;
            }
        }
    }

    private async Task SyncOplnAsync(string tenantId, PollingCursorRepository cursorRepo,
        PriceSnapshotRepository snapshotRepo, HanaOutboxRepository hanaRepo, CancellationToken ct)
    {
        var cursor = await cursorRepo.GetAsync(tenantId, "OPLN", ct);
        if (cursor == null)
        {
            cursor = new PollingCursor
            {
                TenantId = tenantId,
                EntityType = "OPLN",
                LastUpdateDate = DateTime.UtcNow.AddDays(-_config.Value.InitialLookbackDays).Date,
                LastUpdateTs = 0,
                LastRunAt = DateTime.UtcNow
            };
        }

        var sinceDate = cursor.LastUpdateDate.Date;
        var rows = await QueryOplnAsync(sinceDate, _config.Value.BatchSize, ct);
        if (rows.Count == 0)
        {
            _logger.LogDebug("No OPLN changes since {Date}", sinceDate);
            cursor.LastRunAt = DateTime.UtcNow;
            await cursorRepo.UpsertAsync(cursor, ct);
            return;
        }

        _logger.LogInformation("OPLN: {Count} rows changed since {Date}", rows.Count, sinceDate);

        var changes = new List<(PriceListHeaderPayload Header, string Hash)>();
        var snapshotsToUpdate = new List<PriceSnapshot>();
        DateTime maxUpdateDate = sinceDate;

        foreach (var row in rows)
        {
            var hash = ComputeOplnHash(row);
            var snapshot = await snapshotRepo.GetAsync(tenantId, $"OPLN:{row.ListNum}", row.ListNum, ct);

            if (snapshot == null || snapshot.PriceHash != hash)
            {
                var header = new PriceListHeaderPayload
                {
                    ListNum = row.ListNum,
                    ListName = row.ListName ?? string.Empty,
                    BaseNum = row.BaseNum,
                    Factor = row.Factor,
                    RoundSys = row.RoundSys,
                    GroupCode = row.GroupCode,
                    SppCounter = row.SppCounter,
                    IsGrossPrc = row.IsGrossPrc == "Y",
                    UpdateDate = row.UpdateDate,
                    ValidFrom = row.ValidFrom,
                    ValidTo = row.ValidTo,
                    PrimCurr = row.PrimCurr,
                    AddCurr1 = row.AddCurr1,
                    AddCurr2 = row.AddCurr2
                };
                changes.Add((header, hash));
            }

            snapshotsToUpdate.Add(new PriceSnapshot
            {
                TenantId = tenantId,
                ItemCode = $"OPLN:{row.ListNum}",
                PriceList = row.ListNum,
                Price = row.Factor ?? 0m,
                Currency = row.PrimCurr ?? "",
                DiscountPercent = 0m,
                PriceHash = hash,
                SapUpdateDate = DateTime.SpecifyKind(row.UpdateDate, DateTimeKind.Utc),
                LastSyncedAt = DateTime.UtcNow
            });

            if (row.UpdateDate > maxUpdateDate)
                maxUpdateDate = row.UpdateDate;
        }

        if (changes.Count > 0)
        {
            foreach (var (header, hash) in changes)
            {
                var json = JsonSerializer.Serialize(header, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var aggregateId = $"{header.ListNum}_{hash[..8]}";
                await hanaRepo.InsertPriceListHeaderEventAsync(tenantId, aggregateId, json, ct);
                _logger.LogInformation("Enqueued PriceListHeaderChanged ListNum={ListNum}, Name={ListName}, AggregateId={AggregateId}", header.ListNum, header.ListName, aggregateId);
            }
            await snapshotRepo.UpsertBatchAsync(snapshotsToUpdate, ct);
        }

        cursor.LastUpdateDate = DateTime.SpecifyKind(maxUpdateDate.Date, DateTimeKind.Utc);
        cursor.LastRunAt = DateTime.UtcNow;
        await cursorRepo.UpsertAsync(cursor, ct);
    }

    private async Task SyncOsppAsync(string tenantId, PollingCursorRepository cursorRepo,
        PriceSnapshotRepository snapshotRepo, HanaOutboxRepository hanaRepo, CancellationToken ct)
    {
        var cursor = await cursorRepo.GetAsync(tenantId, "OSPP", ct);
        if (cursor == null)
        {
            cursor = new PollingCursor
            {
                TenantId = tenantId,
                EntityType = "OSPP",
                LastUpdateDate = DateTime.UtcNow.AddDays(-_config.Value.InitialLookbackDays).Date,
                LastUpdateTs = 0,
                LastRunAt = DateTime.UtcNow
            };
        }

        var sinceDate = cursor.LastUpdateDate.Date;
        var rows = await QueryOsppAsync(sinceDate, _config.Value.BatchSize, ct);
        if (rows.Count == 0)
        {
            _logger.LogDebug("No OSPP changes since {Date}", sinceDate);
            cursor.LastRunAt = DateTime.UtcNow;
            await cursorRepo.UpsertAsync(cursor, ct);
            return;
        }

        _logger.LogInformation("OSPP: {Count} rows changed since {Date}", rows.Count, sinceDate);

        var changes = new List<PriceListItem>();
        var snapshotsToUpdate = new List<PriceSnapshot>();
        DateTime maxUpdateDate = sinceDate;

        foreach (var row in rows)
        {
            // 1. ALWAYS read full SPP1 + SPP2 hierarchy to detect changes at any level
            var spp1Rows = await QuerySpp1Async(row.ItemCode, row.CardCode, ct);
            var spp2Rows = new List<Spp2Row>();
            foreach (var spp1 in spp1Rows)
            {
                var s2 = await QuerySpp2Async(row.ItemCode, row.CardCode, spp1.LineNum, ct);
                spp2Rows.AddRange(s2);
            }

            // 2. Build complete item with full hierarchy
            var item = new PriceListItem
            {
                ItemCode = row.ItemCode,
                CardCode = row.CardCode,
                ListNum = row.ListNum,
                Price = row.Price,
                Currency = row.Currency ?? "",
                Discount = row.Discount,
                OsppUpdateDate = row.UpdateDate,
                Periods = spp1Rows.Select(s => new Spp1Period
                {
                    LineNum = s.LineNum,
                    Price = s.Price,
                    Currency = s.Currency ?? "",
                    Discount = s.Discount,
                    FromDate = s.FromDate,
                    ToDate = s.ToDate,
                    AutoUpdt = s.AutoUpdt == "Y",
                    Expand = s.Expand == "Y"
                }).ToList(),
                QuantityDiscounts = spp2Rows.Select(s => new Spp2QuantityDiscount
                {
                    Spp1LineNum = s.Spp1LineNum,
                    Spp2LineNum = s.Spp2LineNum,
                    Amount = s.Amount,
                    Price = s.Price,
                    Currency = s.Currency ?? "",
                    Discount = s.Discount,
                    UomEntry = s.UomEntry
                }).ToList()
            };

            // 3. Combined hash OSPP + SPP1 + SPP2 to detect changes at any level
            var hash = ComputeCombinedHash(row, spp1Rows, spp2Rows);
            var snapshot = await snapshotRepo.GetAsync(tenantId, $"OSPP:{row.ItemCode}:{row.CardCode}", row.ListNum, ct);

            if (snapshot == null || snapshot.PriceHash != hash)
            {
                changes.Add(item);
            }

            snapshotsToUpdate.Add(new PriceSnapshot
            {
                TenantId = tenantId,
                ItemCode = $"OSPP:{row.ItemCode}:{row.CardCode}",
                PriceList = row.ListNum,
                Price = row.Price,
                Currency = row.Currency ?? "",
                DiscountPercent = row.Discount,
                PriceHash = hash,
                SapUpdateDate = DateTime.SpecifyKind(row.UpdateDate, DateTimeKind.Utc),
                LastSyncedAt = DateTime.UtcNow
            });

            if (row.UpdateDate > maxUpdateDate)
                maxUpdateDate = row.UpdateDate;
        }

        if (changes.Count > 0)
        {
            await EnqueueChangesAsync(tenantId, changes, hanaRepo, ct);
            await snapshotRepo.UpsertBatchAsync(snapshotsToUpdate, ct);
        }

        cursor.LastUpdateDate = DateTime.SpecifyKind(maxUpdateDate.Date, DateTimeKind.Utc);
        cursor.LastRunAt = DateTime.UtcNow;
        await cursorRepo.UpsertAsync(cursor, ct);
    }

    private async Task SyncItm1Async(string tenantId, PriceSnapshotRepository snapshotRepo,
        HanaOutboxRepository hanaRepo, CancellationToken ct)
    {
        _logger.LogInformation("ITM1: starting full scan");
        var rows = await QueryItm1FullAsync(_config.Value.BatchSize, ct);
        if (rows.Count == 0) return;

        var changes = new List<PriceListItem>();
        var snapshotsToUpdate = new List<PriceSnapshot>();

        foreach (var row in rows)
        {
            var hash = ComputeItm1Hash(row);
            var snapshot = await snapshotRepo.GetAsync(tenantId, $"ITM1:{row.ItemCode}", row.PriceList, ct);

            if (snapshot == null || snapshot.PriceHash != hash)
            {
                changes.Add(new PriceListItem
                {
                    ItemCode = row.ItemCode,
                    ListNum = row.PriceList,
                    Price = row.Price,
                    Currency = row.Currency ?? "",
                    Discount = row.Discount
                });
            }

            snapshotsToUpdate.Add(new PriceSnapshot
            {
                TenantId = tenantId,
                ItemCode = $"ITM1:{row.ItemCode}",
                PriceList = row.PriceList,
                Price = row.Price,
                Currency = row.Currency ?? "",
                DiscountPercent = row.Discount,
                PriceHash = hash,
                LastSyncedAt = DateTime.UtcNow
            });
        }

        if (changes.Count > 0)
        {
            await EnqueueChangesAsync(tenantId, changes, hanaRepo, ct);
            await snapshotRepo.UpsertBatchAsync(snapshotsToUpdate, ct);
        }

        _logger.LogInformation("ITM1: full scan completed. {Count} changes detected.", changes.Count);
    }

    private async Task EnqueueChangesAsync(string tenantId, List<PriceListItem> changes,
        HanaOutboxRepository hanaRepo, CancellationToken ct)
    {
        var groupBy = _config.Value.GroupBy;
        var grouped = groupBy == "CardCode"
            ? changes.GroupBy(c => c.CardCode ?? "_NO_CARD_")
            : changes.GroupBy(c => c.ListNum.ToString());

        foreach (var group in grouped)
        {
            var groupKey = group.Key;
            var items = group.ToList();
            var chunks = items.Chunk(_config.Value.MaxPayloadItems);
            var batchCount = chunks.Count();
            var batchIndex = 0;

            foreach (var chunk in chunks)
            {
                var listNum = groupBy == "ListNum" ? int.Parse(groupKey) : 0;
                var cardCode = groupBy == "CardCode" ? groupKey : null;

                var payload = new PriceListChangedPayload
                {
                    ListNum = listNum,
                    CardCode = cardCode,
                    GroupBy = groupBy,
                    Items = chunk.ToList(),
                    IsFullSync = false,
                    BatchIndex = batchIndex,
                    BatchCount = batchCount,
                    SyncDate = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var aggregateId = batchCount > 1 ? $"{groupKey}_{batchIndex}" : groupKey;

                await hanaRepo.InsertPriceListEventAsync(tenantId, aggregateId, json, ct);
                _logger.LogInformation("Enqueued PriceListChanged group={Group}, batch={BatchIndex}/{BatchCount}, items={Count}",
                    groupKey, batchIndex, batchCount, chunk.Length);

                batchIndex++;
            }
        }
    }
    private async Task<IReadOnlyList<OsppRow>> QueryOsppAsync(DateTime sinceDate, int batchSize, CancellationToken ct)
    {
        var dateStr = sinceDate.ToString("yyyy-MM-dd");
        const string sql = @"
            SELECT 
                ""ItemCode"",
                ""CardCode"",
                ""Price"",
                ""Currency"",
                ""Discount"",
                ""ListNum"",
                ""UpdateDate""
            FROM VIAGGIO_QA.OSPP
            WHERE ""UpdateDate"" >= ?
            ORDER BY ""UpdateDate"" ASC
            LIMIT ?;
        ";

        using var lease = await _hanaPool.AcquireAsync(ct);
        var connection = lease.Connection;
        var results = await connection.QueryAsync<OsppRow>(sql, new { SinceDate = dateStr, Limit = batchSize });
        return results.ToList();
    }

    private async Task<IReadOnlyList<Spp1Row>> QuerySpp1Async(string itemCode, string cardCode, CancellationToken ct)
    {
        const string sql = @"
            SELECT 
                ""ItemCode"",
                ""CardCode"",
                ""LINENUM"",
                ""Price"",
                ""Currency"",
                ""Discount"",
                ""ListNum"",
                ""FromDate"",
                ""ToDate"",
                ""AutoUpdt"",
                ""Expand""
            FROM VIAGGIO_QA.SPP1
            WHERE ""ItemCode"" = ? AND ""CardCode"" = ?
            ORDER BY ""LINENUM"" ASC;
        ";

        using var lease = await _hanaPool.AcquireAsync(ct);
        var connection = lease.Connection;
        var results = await connection.QueryAsync<Spp1Row>(sql, new { ItemCode = itemCode, CardCode = cardCode });
        return results.ToList();
    }

    private async Task<IReadOnlyList<Spp2Row>> QuerySpp2Async(string itemCode, string cardCode, int spp1LineNum, CancellationToken ct)
    {
        const string sql = @"
            SELECT 
                ""ItemCode"",
                ""CardCode"",
                ""SPP1LNum"",
                ""SPP2LNum"",
                ""Amount"",
                ""Price"",
                ""Currency"",
                ""Discount"",
                ""UomEntry""
            FROM VIAGGIO_QA.SPP2
            WHERE ""ItemCode"" = ? AND ""CardCode"" = ? AND ""SPP1LNum"" = ?
            ORDER BY ""SPP2LNum"" ASC;
        ";

        using var lease = await _hanaPool.AcquireAsync(ct);
        var connection = lease.Connection;
        var results = await connection.QueryAsync<Spp2Row>(sql, new { ItemCode = itemCode, CardCode = cardCode, Spp1LNum = spp1LineNum });
        return results.ToList();
    }

    private async Task<IReadOnlyList<Itm1Row>> QueryItm1FullAsync(int batchSize, CancellationToken ct)
    {
        const string sql = @"
            SELECT 
                ""ItemCode"",
                ""PriceList"",
                ""Price"",
                ""Currency"",
                ""Discount""
            FROM VIAGGIO_QA.ITM1
            LIMIT ?;
        ";

        using var lease = await _hanaPool.AcquireAsync(ct);
        var connection = lease.Connection;
        var results = await connection.QueryAsync<Itm1Row>(sql, new { Limit = batchSize });
        return results.ToList();
    }

    private async Task<IReadOnlyList<OplnRow>> QueryOplnAsync(DateTime sinceDate, int batchSize, CancellationToken ct)
    {
        var dateStr = sinceDate.ToString("yyyy-MM-dd");
        const string sql = @"
            SELECT 
                ""ListNum"",
                ""ListName"",
                ""BASE_NUM"",
                ""Factor"",
                ""RoundSys"",
                ""GroupCode"",
                ""SPPCounter"",
                ""IsGrossPrc"",
                ""UpdateDate"",
                ""ValidFor"",
                ""ValidFrom"",
                ""ValidTo"",
                ""PrimCurr"",
                ""AddCurr1"",
                ""AddCurr2""
            FROM VIAGGIO_QA.OPLN
            WHERE ""UpdateDate"" >= ?
            ORDER BY ""UpdateDate"" ASC
            LIMIT ?;
        ";

        using var lease = await _hanaPool.AcquireAsync(ct);
        var connection = lease.Connection;
        var results = await connection.QueryAsync<OplnRow>(sql, new { SinceDate = dateStr, Limit = batchSize });
        return results.ToList();
    }

    private static string ComputeCombinedHash(OsppRow row, IReadOnlyList<Spp1Row> spp1Rows, IReadOnlyList<Spp2Row> spp2Rows)
    {
        var sb = new StringBuilder();
        sb.Append($"{row.Price:F4}|{row.Currency}|{row.Discount:F4}");

        foreach (var s1 in spp1Rows.OrderBy(s => s.LineNum))
        {
            sb.Append($"|S1:{s1.LineNum}:{s1.Price:F4}:{s1.Currency}:{s1.Discount:F4}:{s1.FromDate:yyyyMMdd}:{s1.ToDate:yyyyMMdd}:{s1.AutoUpdt == "Y"}:{s1.Expand == "Y"}");
        }

        foreach (var s2 in spp2Rows.OrderBy(s => s.Spp1LineNum).ThenBy(s => s.Spp2LineNum))
        {
            sb.Append($"|S2:{s2.Spp1LineNum}:{s2.Spp2LineNum}:{s2.Amount:F4}:{s2.Price:F4}:{s2.Currency}:{s2.Discount:F4}:{s2.UomEntry}");
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ComputeItm1Hash(Itm1Row row)
    {
        var input = $"{row.Price:F4}|{row.Currency}|{row.Discount:F4}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ComputeOplnHash(OplnRow row)
    {
        var input = $"{row.ListName}|{row.BaseNum}|{row.Factor:F6}|{row.RoundSys}|{row.GroupCode}|{row.SppCounter}|{row.IsGrossPrc}|{row.ValidFrom:yyyyMMdd}|{row.ValidTo:yyyyMMdd}|{row.PrimCurr}|{row.AddCurr1}|{row.AddCurr2}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private class OsppRow
    {
        public string ItemCode { get; set; } = string.Empty;
        public string CardCode { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public string? Currency { get; set; }
        public decimal Discount { get; set; }
        public int ListNum { get; set; }
        public DateTime UpdateDate { get; set; }
    }

    private class Spp1Row
    {
        public string ItemCode { get; set; } = string.Empty;
        public string CardCode { get; set; } = string.Empty;
        public int LineNum { get; set; }
        public decimal Price { get; set; }
        public string? Currency { get; set; }
        public decimal Discount { get; set; }
        public int ListNum { get; set; }
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string? AutoUpdt { get; set; }
        public string? Expand { get; set; }
    }

    private class Spp2Row
    {
        public string ItemCode { get; set; } = string.Empty;
        public string CardCode { get; set; } = string.Empty;
        public int Spp1LineNum { get; set; }
        public int Spp2LineNum { get; set; }
        public decimal Amount { get; set; }
        public decimal Price { get; set; }
        public string? Currency { get; set; }
        public decimal Discount { get; set; }
        public int? UomEntry { get; set; }
    }

    private class Itm1Row
    {
        public string ItemCode { get; set; } = string.Empty;
        public int PriceList { get; set; }
        public decimal Price { get; set; }
        public string? Currency { get; set; }
        public decimal Discount { get; set; }
    }

    private class OplnRow
    {
        public int ListNum { get; set; }
        public string? ListName { get; set; }
        public int? BaseNum { get; set; }
        public decimal? Factor { get; set; }
        public string? RoundSys { get; set; }
        public int? GroupCode { get; set; }
        public int? SppCounter { get; set; }
        public string? IsGrossPrc { get; set; }
        public DateTime UpdateDate { get; set; }
        public string? ValidFor { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public string? PrimCurr { get; set; }
        public string? AddCurr1 { get; set; }
        public string? AddCurr2 { get; set; }
    }
}
