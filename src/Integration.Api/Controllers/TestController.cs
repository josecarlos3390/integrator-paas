using Dapper;
using Integration.Shared.Configuration;
using Integration.Shared.Infrastructure;
using Integration.Shared.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Data.Odbc;

namespace Integration.Api.Controllers;

/// <summary>
/// Utility endpoints to test connections and simulate events
/// during development and integration testing.
/// </summary>
[ApiController]
[Route("api/test")]
public class TestController : ControllerBase
{
    private readonly HanaOutboxRepository _hanaRepo;
    private readonly HanaConnectionPool _hanaPool;
    private readonly IOptions<HanaConfig> _hanaConfig;
    private readonly ILogger<TestController> _logger;

    public TestController(
        HanaOutboxRepository hanaRepo,
        HanaConnectionPool hanaPool,
        IOptions<HanaConfig> hanaConfig,
        ILogger<TestController> logger)
    {
        _hanaRepo = hanaRepo;
        _hanaPool = hanaPool;
        _hanaConfig = hanaConfig;
        _logger = logger;
    }

    /// <summary>
    /// Verifies connectivity to SAP HANA by running SELECT 1.
    /// </summary>
    [HttpGet("hana-health")]
    public async Task<IActionResult> CheckHanaHealth(CancellationToken ct)
    {
        try
        {
            using var connection = new OdbcConnection(_hanaConfig.Value.ConnectionString);
            await connection.OpenAsync(ct);

            using var command = new OdbcCommand("SELECT 1 FROM DUMMY", connection);
            var result = await command.ExecuteScalarAsync(ct);

            return Ok(new
            {
                Connected = true,
                ServerVersion = connection.ServerVersion,
                Timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HANA health check failed");
            return StatusCode(502, new
            {
                Connected = false,
                Error = ex.Message,
                Timestamp = DateTime.UtcNow
            });
        }
    }

    /// <summary>
    /// Inserts a test event into INTEGRATION_BUS.OUTBOX_EVENTS in HANA,
    /// simulating that the SAP Add-on just created an invoice.
    /// </summary>
    [HttpPost("simulate-invoice")]
    public async Task<IActionResult> SimulateInvoice(
        [FromQuery] int docEntry = 12345,
        [FromQuery] string? tenantId = null,
        CancellationToken ct = default)
    {
        tenantId ??= HttpContext.Items["TenantId"]?.ToString() ?? "tenant-001";

        try
        {
            using var connection = new OdbcConnection(_hanaConfig.Value.ConnectionString);
            await connection.OpenAsync(ct);

            var id = Guid.NewGuid().ToString();
            var sql = $"INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS (ID, TENANT_ID, EVENT_TYPE, AGGREGATE_ID, OCCURRED_AT, PROCESSED_AT, ATTEMPT_COUNT, ERROR_MESSAGE, IS_DEAD_LETTER) VALUES ('{id}', '{tenantId.Replace("'", "''")}', 'InvoiceCreated', '{docEntry.ToString().Replace("'", "''")}', CURRENT_TIMESTAMP, NULL, 0, NULL, 0)";

            using var command = new OdbcCommand(sql, connection);
            var rows = command.ExecuteNonQuery();

            return Ok(new
            {
                Message = "Invoice event simulated in HANA outbox",
                DocEntry = docEntry,
                TenantId = tenantId,
                RowsAffected = rows
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to simulate invoice event in HANA");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Inserts a customer test event into INTEGRATION_BUS.OUTBOX_EVENTS in HANA,
    /// simulating that the SAP Add-on detected a BusinessPartner creation or update.
    /// </summary>
    [HttpPost("simulate-customer")]
    public async Task<IActionResult> SimulateCustomer(
        [FromQuery] string cardCode = "C00001",
        [FromQuery] string eventType = "CustomerCreated",
        [FromQuery] string? tenantId = null,
        CancellationToken ct = default)
    {
        tenantId ??= HttpContext.Items["TenantId"]?.ToString() ?? "tenant-001";

        try
        {
            using var connection = new OdbcConnection(_hanaConfig.Value.ConnectionString);
            await connection.OpenAsync(ct);

            var id = Guid.NewGuid().ToString();
            var sql = $"INSERT INTO INTEGRATION_BUS.OUTBOX_EVENTS (ID, TENANT_ID, EVENT_TYPE, AGGREGATE_ID, OCCURRED_AT, PROCESSED_AT, ATTEMPT_COUNT, ERROR_MESSAGE, IS_DEAD_LETTER) VALUES ('{id}', '{tenantId.Replace("'", "''")}', '{eventType.Replace("'", "''")}', '{cardCode.Replace("'", "''")}', CURRENT_TIMESTAMP, NULL, 0, NULL, 0)";

            using var command = new OdbcCommand(sql, connection);
            var rows = command.ExecuteNonQuery();

            return Ok(new
            {
                Message = "Customer event simulated in HANA outbox",
                CardCode = cardCode,
                EventType = eventType,
                TenantId = tenantId,
                RowsAffected = rows
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to simulate customer event in HANA");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Lists Dead Letter events from PostgreSQL (for operations).
    /// </summary>
    [HttpGet("dead-letters")]
    public async Task<IActionResult> GetDeadLetters(
        [FromQuery] int take = 20,
        CancellationToken ct = default)
    {
        try
        {
            // Inject the repo via HttpContext.RequestServices to avoid changing the constructor
            var dlqRepo = HttpContext.RequestServices.GetRequiredService<Integration.Shared.Repositories.DeadLetterRepository>();
            var tenantId = HttpContext.Items["TenantId"]?.ToString() ?? "tenant-001";
            var (events, totalCount) = await dlqRepo.GetByTenantAsync(tenantId, 0, take, ct);
            return Ok(new { Items = events, TotalCount = totalCount, Page = 1, PageSize = take });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch dead letters");
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Reprocesses an event that was moved to Dead Letter (IS_DEAD_LETTER = 1)
    /// resetting its counters so the Worker can retry it.
    /// </summary>
    [HttpPost("retry-dead-letter")]
    public async Task<IActionResult> RetryDeadLetter(
        [FromQuery] string eventId,
        CancellationToken ct = default)
    {
        try
        {
            using var connection = new OdbcConnection(_hanaConfig.Value.ConnectionString);
            await connection.OpenAsync(ct);

            var sql = $@"
                UPDATE INTEGRATION_BUS.OUTBOX_EVENTS
                SET IS_DEAD_LETTER = 0,
                    ATTEMPT_COUNT = 0,
                    ERROR_MESSAGE = NULL,
                    PROCESSED_AT = NULL
                WHERE ID = '{eventId.Replace("'", "''")}';
            ";

            using var command = new OdbcCommand(sql, connection);
            var rows = command.ExecuteNonQuery();

            if (rows == 0)
                return NotFound(new { Message = "Event not found in HANA" });

            return Ok(new { Message = "Event queued for retry", EventId = eventId });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry dead letter event {EventId}", eventId);
            return StatusCode(500, new { Error = ex.Message });
        }
    }

    /// <summary>
    /// Lists current pending events in HANA (useful to verify
    /// that the Add-on is writing or that the dispatcher is reading).
    /// </summary>
    [HttpGet("pending-events")]
    public async Task<IActionResult> GetPendingEvents(
        [FromQuery] int take = 10,
        CancellationToken ct = default)
    {
        try
        {
            var events = await _hanaRepo.FetchPendingAsync(take, maxAttempts: 5, ct);
            return Ok(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch pending events from HANA");
            return StatusCode(500, new { Error = ex.Message });
        }
    }
}
