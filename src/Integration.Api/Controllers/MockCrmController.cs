using Integration.Shared.Dtos;
using Microsoft.AspNetCore.Mvc;

namespace Integration.Api.Controllers;

/// <summary>
/// Mock of the external CRM for end-to-end testing of the SAP→CRM flow.
/// Simulates the endpoints that the real CRM should expose.
/// </summary>
[ApiController]
[Route("api/mock/crm")]
public class MockCrmController : ControllerBase
{
    private readonly ILogger<MockCrmController> _logger;

    public MockCrmController(ILogger<MockCrmController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Receives an invoice synchronized from SAP.
    /// Simulates successful creation (201) or idempotency (409).
    /// </summary>
    [HttpPost("invoices")]
    public IActionResult CreateInvoice([FromBody] object payload)
    {
        _logger.LogInformation("[MOCK CRM] Invoice received: {@Payload}", payload);

        var externalId = ExtractExternalId(payload);

        // Simulate CRM down: 500 for DocEntry 77777
        if (externalId == "77777")
        {
            _logger.LogError("[MOCK CRM] Simulating CRM failure (500) for {ExternalId}", externalId);
            return StatusCode(500, new { Message = "CRM internal error", ExternalId = externalId });
        }

        // Simulate idempotency: 409 for 10002
        if (externalId == "10002")
        {
            _logger.LogWarning("[MOCK CRM] Invoice {ExternalId} already exists (409)", externalId);
            return Conflict(new { Message = "Invoice already exists", ExternalId = externalId });
        }

        _logger.LogInformation("[MOCK CRM] Invoice {ExternalId} created successfully", externalId);
        return StatusCode(201, new { Id = Guid.NewGuid(), ExternalId = externalId, Status = "created" });
    }

    /// <summary>
    /// Receives the callback of an order processed asynchronously.
    /// </summary>
    [HttpPost("callbacks/order-result")]
    public IActionResult OrderResult([FromBody] object payload)
    {
        _logger.LogInformation("[MOCK CRM] Async order result received: {@Payload}", payload);
        return Ok(new { Received = true });
    }

    /// <summary>
    /// Receives a customer synchronized from SAP.
    /// </summary>
    private static readonly List<CrmCustomerPayload> _customers = new();

    [HttpPost("customers")]
    public IActionResult CreateCustomer([FromBody] CrmCustomerPayload payload)
    {
        _logger.LogInformation("[MOCK CRM] Customer received: {ExternalId} - {Name}", payload.ExternalId, payload.Name);
        _logger.LogInformation("[MOCK CRM] Customer details: {@Payload}", payload);
        _customers.Add(payload);
        return StatusCode(201, new { Id = Guid.NewGuid(), ExternalId = payload.ExternalId, Status = "created" });
    }

    /// <summary>
    /// Receives a vendor synchronized from SAP.
    /// </summary>
    private static readonly List<CrmCustomerPayload> _vendors = new();

    [HttpPost("vendors")]
    public IActionResult CreateVendor([FromBody] CrmCustomerPayload payload)
    {
        _logger.LogInformation("[MOCK CRM] Vendor received: {ExternalId} - {Name}", payload.ExternalId, payload.Name);
        _vendors.Add(payload);
        return StatusCode(201, new { Id = Guid.NewGuid(), ExternalId = payload.ExternalId, Status = "created" });
    }

    /// <summary>
    /// Lists received customers (for development verification only).
    /// </summary>
    [HttpGet("customers")]
    public IActionResult GetCustomers()
    {
        return Ok(new
        {
            Count = _customers.Count,
            Customers = _customers.Select(c => new
            {
                c.ExternalId,
                c.Name,
                c.Email,
                c.Phone,
                c.Country,
                c.City,
                c.TaxId,
                AddressCount = c.Addresses.Count,
                ContactCount = c.Contacts.Count
            })
        });
    }

    /// <summary>
    /// Receives a batch of price list changes from SAP.
    /// Simulates bulk price creation/update.
    /// </summary>
    [HttpPost("price-lists/batch-update")]
    public IActionResult UpdatePriceListBatch([FromBody] Integration.Shared.Domain.PriceListChangedPayload payload)
    {
        _logger.LogInformation("[MOCK CRM] PriceList batch update received. ListNum={ListNum}, CardCode={CardCode}, Items={Count}, Batch={BatchIndex}/{BatchCount}",
            payload.ListNum, payload.CardCode, payload.Items.Count, payload.BatchIndex, payload.BatchCount);

        // Simulate 500 for ListNum 777
        if (payload.ListNum == 777)
        {
            _logger.LogError("[MOCK CRM] Simulating CRM failure (500) for ListNum {ListNum}", payload.ListNum);
            return StatusCode(500, new { Message = "CRM internal error", ListNum = payload.ListNum });
        }

        // Simulate 409 idempotency if batchIndex 0 and listNum is even
        if (payload.BatchIndex == 0 && payload.ListNum % 2 == 0 && !payload.IsFullSync)
        {
            _logger.LogWarning("[MOCK CRM] PriceList batch {ListNum} already processed (409)", payload.ListNum);
            return Conflict(new { Message = "Batch already processed", ListNum = payload.ListNum });
        }

        return Ok(new
        {
            ListNum = payload.ListNum,
            CardCode = payload.CardCode,
            ItemsProcessed = payload.Items.Count,
            Status = "updated",
            ProcessedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Receives the header (metadata) of a price list from SAP.
    /// </summary>
    [HttpPost("price-lists/header-update")]
    public IActionResult UpdatePriceListHeader([FromBody] Integration.Shared.Domain.PriceListHeaderPayload payload)
    {
        _logger.LogInformation("[MOCK CRM] PriceList header update received. ListNum={ListNum}, Name={ListName}, Factor={Factor}",
            payload.ListNum, payload.ListName, payload.Factor);

        // Simulate 409 idempotency if ListNum is even
        if (payload.ListNum % 2 == 0)
        {
            _logger.LogWarning("[MOCK CRM] PriceList header {ListNum} already processed (409)", payload.ListNum);
            return Conflict(new { Message = "Header already processed", ListNum = payload.ListNum });
        }

        return Ok(new
        {
            ListNum = payload.ListNum,
            ListName = payload.ListName,
            Status = "header_updated",
            ProcessedAt = DateTime.UtcNow
        });
    }

    private static string? ExtractExternalId(object payload)
    {
        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(payload);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("externalId", out var prop))
                return prop.GetString();
            if (doc.RootElement.TryGetProperty("ExternalId", out var prop2))
                return prop2.GetString();
        }
        catch { }
        return null;
    }
}
