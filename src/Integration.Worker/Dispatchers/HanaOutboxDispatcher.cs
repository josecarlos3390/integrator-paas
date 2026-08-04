using System.Collections.Concurrent;
using System.Text.Json;
using Integration.Shared.Clients;
using Integration.Shared.Configuration;
using Integration.Shared.Connectors;
using Integration.Shared.Connectors.HansaCrm;
using Integration.Shared.Domain;
using Integration.Shared.Dtos;
using Integration.Shared.Exceptions;
using Integration.Shared.Mappers;
using Integration.Shared.Observability;
using Integration.Shared.Repositories;
using Integration.Shared.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Refit;

namespace Integration.Worker.Dispatchers;

/// <summary>
/// Orchestrates reading the HANA outbox, fetching data from
/// the SAP Service Layer, its transformation and delivery to the external CRM.
/// </summary>
public class HanaOutboxDispatcher
{
    private readonly HanaOutboxRepository _hanaRepo;
    private readonly ITenantClientFactory _clientFactory;
    private readonly IntegrationLogRepository _logRepo;
    private readonly DeadLetterRepository _deadLetterRepo;
    private readonly ITenantFeatureService _featureService;
    private readonly IAlertingService _alertingService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IOptions<OutboxConfig> _config;
    private readonly TenantQuotaRepository _quotaRepo;
    private readonly IOptions<HansaCrmConfig> _hansaConfig;
    private readonly ILogger<HanaOutboxDispatcher> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly VendorBankSnapshotRepository _vendorBankRepo;
    private readonly ITelegramNotifier _telegramNotifier;

    /// <summary>
    /// Name of the HANA server this dispatcher is polling (multi-HANA).
    /// Informational only — used to identify the server in log messages.
    /// </summary>
    public string ServerName { get; set; } = "default";

    public HanaOutboxDispatcher(
        HanaOutboxRepository hanaRepo,
        ITenantClientFactory clientFactory,
        IntegrationLogRepository logRepo,
        DeadLetterRepository deadLetterRepo,
        ITenantFeatureService featureService,
        IAlertingService alertingService,
        IIdempotencyService idempotencyService,
        TenantQuotaRepository quotaRepo,
        IOptions<OutboxConfig> config,
        IOptions<HansaCrmConfig> hansaConfig,
        ILogger<HanaOutboxDispatcher> logger,
        IServiceScopeFactory scopeFactory,
        VendorBankSnapshotRepository vendorBankRepo,
        ITelegramNotifier telegramNotifier)
    {
        _hanaRepo = hanaRepo;
        _clientFactory = clientFactory;
        _logRepo = logRepo;
        _deadLetterRepo = deadLetterRepo;
        _featureService = featureService;
        _alertingService = alertingService;
        _idempotencyService = idempotencyService;
        _quotaRepo = quotaRepo;
        _config = config;
        _hansaConfig = hansaConfig;
        _logger = logger;
        _scopeFactory = scopeFactory;
        _vendorBankRepo = vendorBankRepo;
        _telegramNotifier = telegramNotifier;
    }

    /// <summary>
    /// Executes a processing cycle: reads a batch from HANA and processes it.
    /// </summary>
    public async Task RunCycleAsync(CancellationToken ct = default)
    {
        var batch = await _hanaRepo.FetchPendingAsync(_config.Value.BatchSize, _config.Value.MaxAttempts, ct);
        if (batch.Count == 0) return;

        // Acquire a lease on the events to prevent concurrent processing by other worker instances.
        // Lease duration = polling interval + 30s buffer to cover processing time.
        var leaseDuration = TimeSpan.FromSeconds(_config.Value.PollingSeconds + 30);
        var leasedIds = await _hanaRepo.AcquireLeaseAsync(batch.Select(e => e.Id), leaseDuration, ct);
        var leasedBatch = batch.Where(e => leasedIds.Contains(e.Id)).ToList();

        if (leasedBatch.Count == 0)
        {
            _logger.LogDebug("No outbox events could be leased. Another instance may be processing them.");
            return;
        }

        if (leasedBatch.Count < batch.Count)
        {
            _logger.LogInformation("Leased {Leased} of {Total} outbox events. Others are being processed by another instance.", leasedBatch.Count, batch.Count);
        }
        else
        {
            _logger.LogInformation("Processing {Count} outbox events from HANA server {ServerName}", leasedBatch.Count, ServerName);
        }

        // Accumulate logs during the cycle and flush in a single batch at the end.
        var pendingLogs = new ConcurrentBag<IntegrationLog>();

        // Determine connector type per tenant to decide batch vs individual processing.
        var tenantConnectors = new Dictionary<string, ICrmConnector>();
        var hansaEvents = new List<HanaOutboxEvent>();
        var otherEvents = new List<HanaOutboxEvent>();

        foreach (var evt in leasedBatch)
        {
            if (!tenantConnectors.TryGetValue(evt.TenantId, out var connector))
            {
                connector = await _clientFactory.GetCrmConnectorAsync(evt.TenantId);
                tenantConnectors[evt.TenantId] = connector;
            }

            if (connector is HansaCrmConnector)
                hansaEvents.Add(evt);
            else
                otherEvents.Add(evt);
        }

        // Process non-Hansa events with limited parallelism (max 5 concurrent).
        // Each event runs in its own DI scope so EF DbContext is thread-safe.
        var semaphore = new SemaphoreSlim(5);
        var tasks = otherEvents.Select(async evt =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                // Reuse this cycle's HANA repository so each event is marked in the same
                // server it was read from. Resolving the dispatcher from scoped DI would
                // inject the default-server repository (multi-HANA bug).
                var dispatcher = ActivatorUtilities.CreateInstance<HanaOutboxDispatcher>(scope.ServiceProvider, _hanaRepo);
                dispatcher.ServerName = ServerName;
                await dispatcher.ProcessEventAsync(evt, pendingLogs, ct);

                if (_config.Value.RateLimitDelayMs > 0)
                {
                    await Task.Delay(_config.Value.RateLimitDelayMs, ct);
                }
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks);

        // Process HansaCRM events in batches
        if (hansaEvents.Count > 0)
        {
            await ProcessHansaBatchAsync(hansaEvents, pendingLogs, ct);
        }

        // Flush all accumulated logs in a single batch transaction
        if (pendingLogs.Count > 0)
        {
            try
            {
                await _logRepo.AddBatchAsync(pendingLogs, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush {Count} integration logs in batch", pendingLogs.Count);
            }
        }
    }

    private async Task WriteLogAsync(IntegrationLog log, ConcurrentBag<IntegrationLog>? pendingLogs, CancellationToken ct)
    {
        if (pendingLogs is not null)
            pendingLogs.Add(log);
        else
            await _logRepo.AddAsync(log, ct);
    }

    internal async Task ProcessEventAsync(HanaOutboxEvent evt, ConcurrentBag<IntegrationLog>? pendingLogs, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            var featureKey = _featureService.ResolveFeatureKey(evt.ObjectType);
            var isEnabled = await _featureService.IsEnabledAsync(evt.TenantId, featureKey, ct);
            if (!isEnabled)
            {
                _logger.LogWarning(
                    "Feature {FeatureKey} is disabled for tenant {TenantId}. Skipping event {EventId}.",
                    featureKey, evt.TenantId, evt.Id);
                await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
                sw.Stop();
                IntegrationMetrics.RecordFeatureFlagDecision(evt.TenantId, featureKey, false);
                IntegrationMetrics.RecordEventProcessed(evt.EventType, evt.TenantId, "skipped");
                IntegrationMetrics.RecordEventLatency(evt.EventType, evt.TenantId, sw.ElapsedMilliseconds);
                await WriteLogAsync(new IntegrationLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = evt.TenantId,
                    CorrelationId = correlationId,
                    Direction = IntegrationDirection.SapToCrm,
                    EventType = evt.EventType,
                    ExternalId = evt.AggregateId,
                    Status = "skipped",
                    ErrorMessage = $"Feature {featureKey} disabled for tenant {evt.TenantId}",
                    DurationMs = sw.ElapsedMilliseconds,
                    CreatedAt = DateTime.UtcNow
                }, pendingLogs, ct);
                return;
            }

            // Tenant quota check
            var quota = await _quotaRepo.GetAsync(evt.TenantId, ct);
            if (quota is not null && await IsQuotaExceededAsync(evt.TenantId, quota, ct))
            {
                _logger.LogWarning(
                    "Tenant {TenantId} has exceeded the hourly event quota. Delaying event {EventId}.",
                    evt.TenantId, evt.Id);
                await _hanaRepo.DelayEventAsync(evt.Id, TimeSpan.FromMinutes(5), ct);
                sw.Stop();
                IntegrationMetrics.RecordEventProcessed(evt.EventType, evt.TenantId, "quota_exceeded");
                IntegrationMetrics.RecordEventLatency(evt.EventType, evt.TenantId, sw.ElapsedMilliseconds);
                await WriteLogAsync(new IntegrationLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = evt.TenantId,
                    CorrelationId = correlationId,
                    Direction = IntegrationDirection.SapToCrm,
                    EventType = evt.EventType,
                    ExternalId = evt.AggregateId,
                    Status = "quota_exceeded",
                    ErrorMessage = $"Hourly event quota exceeded for tenant {evt.TenantId}",
                    DurationMs = sw.ElapsedMilliseconds,
                    CreatedAt = DateTime.UtcNow
                }, pendingLogs, ct);
                return;
            }

            _logger.LogInformation(
                "Processing HANA event {EventId} (object={ObjectType}, op={EventType}) for aggregate {AggregateId}",
                evt.Id, evt.ObjectType, evt.EventType, evt.AggregateId);

            var sapClient = await _clientFactory.GetSapClientAsync(evt.TenantId);
            var crmClient = await _clientFactory.GetCrmConnectorAsync(evt.TenantId);

            // VENDOR_BANK_ALERT events are state comparisons, not one-shot documents:
            // every vendor update must be evaluated. The idempotency guard (keyed by
            // tenant+objectType+aggregateId) would swallow all updates but the first.
            var idempotencyResult = evt.ObjectType == "VENDOR_BANK_ALERT"
                ? await ProcessByObjectTypeAsync(evt, sapClient, crmClient, correlationId, ct)
                : await _idempotencyService.TryProcessAsync(
                    evt.TenantId, evt.ObjectType, evt.AggregateId,
                    async () => await ProcessByObjectTypeAsync(evt, sapClient, crmClient, correlationId, ct), ct);

            if (idempotencyResult == IdempotencyResult.AlreadyProcessed)
            {
                _logger.LogInformation("Event {EventId} was already processed. Marking as idempotency hit.", evt.Id);
                await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
                sw.Stop();
                IntegrationMetrics.RecordEventProcessed(evt.EventType, evt.TenantId, "idempotency_hit");
                IntegrationMetrics.RecordEventLatency(evt.EventType, evt.TenantId, sw.ElapsedMilliseconds);
                await WriteLogAsync(new IntegrationLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = evt.TenantId,
                    CorrelationId = correlationId,
                    Direction = IntegrationDirection.SapToCrm,
                    EventType = evt.EventType,
                    ExternalId = evt.AggregateId,
                    Status = "idempotency_hit",
                    DurationMs = sw.ElapsedMilliseconds,
                    CreatedAt = DateTime.UtcNow
                }, pendingLogs, ct);
                return;
            }

            if (idempotencyResult == IdempotencyResult.Failed)
            {
                // The error was already thrown by TryProcessAsync; the outer catch handles it
                return;
            }

            sw.Stop();
            IntegrationMetrics.RecordEventProcessed(evt.EventType, evt.TenantId, "success");
            IntegrationMetrics.RecordEventLatency(evt.EventType, evt.TenantId, sw.ElapsedMilliseconds);
            await WriteLogAsync(new IntegrationLog
            {
                Id = Guid.NewGuid(),
                TenantId = evt.TenantId,
                CorrelationId = correlationId,
                Direction = IntegrationDirection.SapToCrm,
                EventType = evt.EventType,
                ExternalId = evt.AggregateId,
                Status = "success",
                DurationMs = sw.ElapsedMilliseconds,
                CreatedAt = DateTime.UtcNow
            }, pendingLogs, ct);
        }
        catch (ApiException apiEx) when ((int)apiEx.StatusCode >= 400 && (int)apiEx.StatusCode < 500 && apiEx.StatusCode != System.Net.HttpStatusCode.Conflict)
        {
            sw.Stop();
            IntegrationMetrics.RecordEventProcessed(evt.EventType, evt.TenantId, "dead_letter");
            IntegrationMetrics.RecordDeadLetter(evt.EventType, evt.TenantId, "crm_4xx");
            IntegrationMetrics.RecordEventLatency(evt.EventType, evt.TenantId, sw.ElapsedMilliseconds);
            // CRM 4xx error (except 409 which is idempotency) â†’ dead letter, do not retry
            await HandleDeadLetterAsync(evt, correlationId, apiEx.Message, sw.ElapsedMilliseconds, pendingLogs, ct);
        }
        catch (SapIntegrationException sapEx) when (sapEx.IsBusinessError)
        {
            sw.Stop();
            IntegrationMetrics.RecordEventProcessed(evt.EventType, evt.TenantId, "dead_letter");
            IntegrationMetrics.RecordDeadLetter(evt.EventType, evt.TenantId, "sap_business_error");
            IntegrationMetrics.RecordEventLatency(evt.EventType, evt.TenantId, sw.ElapsedMilliseconds);
            // SAP business error (e.g. non-existent CardCode) â†’ dead letter
            await HandleDeadLetterAsync(evt, correlationId, sapEx.Message, sw.ElapsedMilliseconds, pendingLogs, ct);
        }
        catch (Exception ex)
        {
            sw.Stop();
            // Transient errors (network, 5xx, etc.) â†’ increment attempts
            var currentAttempt = evt.AttemptCount + 1;
            _logger.LogError(ex, "Transient error processing event {EventId} (attempt {AttemptCount}/{MaxAttempts})", evt.Id, currentAttempt, _config.Value.MaxAttempts);
            await _hanaRepo.MarkFailedAsync(evt.Id, ex.Message, ct);

            if (IsCircuitBreakerException(ex))
            {
                IntegrationMetrics.RecordCircuitBreakerChange("crm", evt.TenantId, "open");
                await _alertingService.RaiseAlertAsync(
                    AlertType.CircuitBreaker,
                    AlertSeverity.Critical,
                    evt.TenantId,
                    "Circuit breaker opened",
                    $"Circuit breaker detected for {evt.EventType}: {ex.Message}",
                    $"EventId={evt.Id}",
                    ct);
            }

            IntegrationMetrics.RecordEventProcessed(evt.EventType, evt.TenantId, "error");
            IntegrationMetrics.RecordRetry(evt.EventType, evt.TenantId, currentAttempt);
            IntegrationMetrics.RecordEventLatency(evt.EventType, evt.TenantId, sw.ElapsedMilliseconds);

            if (evt.AttemptCount + 1 >= _config.Value.MaxAttempts)
            {
                await PromoteToDeadLetterAsync(evt, correlationId, ex.Message, sw.ElapsedMilliseconds, pendingLogs, ct);
            }

            await WriteLogAsync(new IntegrationLog
            {
                Id = Guid.NewGuid(),
                TenantId = evt.TenantId,
                CorrelationId = correlationId,
                Direction = IntegrationDirection.SapToCrm,
                EventType = evt.EventType,
                ExternalId = evt.AggregateId,
                Status = "error",
                ErrorMessage = ex.Message,
                DurationMs = sw.ElapsedMilliseconds,
                CreatedAt = DateTime.UtcNow
            }, pendingLogs, ct);
        }
    }

    private async Task ProcessInvoiceAsync(HanaOutboxEvent evt, ServiceLayerClient sapClient, ICrmConnector crmClient, string correlationId, CancellationToken ct)
    {
        if (!int.TryParse(evt.AggregateId, out var docEntry))
        {
            throw new InvalidOperationException($"Invalid DocEntry in AggregateId: {evt.AggregateId}");
        }

        // 1. Get complete data from SAP
        var sapInvoice = await sapClient.GetInvoiceAsync(docEntry, ct);

        // 2. Loop prevention: skip invoices that originated from the CRM
        if (sapInvoice.U_SyncOrigin == "CRM")
        {
            _logger.LogInformation(
                "Invoice {DocEntry} has U_SyncOrigin=CRM. Skipping to prevent CRM→SAP→CRM loop.", docEntry);
            await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
            return;
        }

        // 3. Map to CRM format
        var crmPayload = InvoiceMapper.ToCrmPayload(sapInvoice);

        // 3. Send to CRM
        var response = await crmClient.CreateInvoiceAsync(crmPayload, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogInformation("Invoice {DocEntry} already exists in CRM (409). Treating as success.", docEntry);
        }
        else if (!response.IsSuccessStatusCode)
        {
            // If it is not 409 and not success, ApiException or unexpected error
            throw new HttpRequestException($"CRM returned {(int)response.StatusCode}: {response.ErrorMessage}");
        }

        // 4. Mark as processed in HANA
        await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
        _logger.LogInformation("Event {EventId} processed successfully", evt.Id);
    }

    private async Task ProcessBusinessPartnerAsync(HanaOutboxEvent evt, ServiceLayerClient sapClient, ICrmConnector crmClient, string correlationId, CancellationToken ct)
    {
        var cardCode = evt.AggregateId;

        // 1. Get complete data from SAP
        var sapCustomer = await sapClient.GetBusinessPartnerAsync(cardCode, ct);

        // 2. Map to CRM format
        var crmPayload = CustomerMapper.ToCrmPayload(sapCustomer);

        // 3. Send to CRM
        var response = await crmClient.CreateCustomerAsync(crmPayload, ct);

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogInformation("Customer {CardCode} already exists in CRM (409). Treating as success.", cardCode);
        }
        else if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"CRM returned {(int)response.StatusCode}: {response.ErrorMessage}");
        }

        // 4. Mark as processed in HANA
        await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
        _logger.LogInformation("Event {EventId} processed successfully", evt.Id);
    }

    /// <summary>
    /// Routes the event to its handler by object type. Extracted so the
    /// idempotency guard can be bypassed for state-comparison flows.
    /// </summary>
    private async Task<IdempotencyResult> ProcessByObjectTypeAsync(
        HanaOutboxEvent evt, ServiceLayerClient sapClient, ICrmConnector crmClient, string correlationId, CancellationToken ct)
    {
        switch (evt.ObjectType)
        {
            case "2": // BusinessPartners
                await ProcessBusinessPartnerAsync(evt, sapClient, crmClient, correlationId, ct);
                break;
            case "13": // Invoices
                await ProcessInvoiceAsync(evt, sapClient, crmClient, correlationId, ct);
                break;
            case "PRICE_LIST": // Price Lists (polling-based)
                await ProcessPriceListAsync(evt, crmClient, correlationId, ct);
                break;
            case "PRICE_LIST_HEADER": // Price List headers (OPLN)
                await ProcessPriceListHeaderAsync(evt, crmClient, correlationId, ct);
                break;
            case "VENDOR_BANK_ALERT": // Vendor bank account watch (anti-fraud)
                await ProcessVendorBankAlertAsync(evt, sapClient, ct);
                break;
            default:
                _logger.LogWarning("Unknown object type {ObjectType} for event {EventId}", evt.ObjectType, evt.Id);
                await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
                break;
        }

        return IdempotencyResult.Processed;
    }

    /// <summary>
    /// VENDOR_BANK_ALERT flow: compares the vendor's current bank account in SAP
    /// against the stored snapshot. On a real change, sends an anti-fraud alert
    /// via Telegram and updates the snapshot (the new value becomes the baseline).
    /// Events without snapshot learn the baseline silently (no false alarms).
    /// </summary>
    private async Task ProcessVendorBankAlertAsync(HanaOutboxEvent evt, ServiceLayerClient sapClient, CancellationToken ct)
    {
        var cardCode = evt.AggregateId;

        // 1. Get current bank data from SAP
        var bp = await sapClient.GetVendorBankInfoAsync(cardCode, ct);

        // Only suppliers are relevant for this flow (the SP already filters, double-check here)
        if (bp.CardType != "cSupplier")
        {
            _logger.LogInformation("Event {EventId}: {CardCode} is not a supplier ({CardType}). Skipping.", evt.Id, cardCode, bp.CardType);
            await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
            return;
        }

        var snapshot = await _vendorBankRepo.GetAsync(evt.TenantId, cardCode, ct);
        var currentSignature = VendorBankSnapshot.BuildSignature(bp.DefaultBankCode, bp.DefaultBranch, bp.DefaultAccount, bp.IBAN)
            + "//" + VendorBankSnapshot.BuildAccountsSignature(bp.BPBankAccounts);
        var newSnapshot = new VendorBankSnapshot
        {
            TenantId = evt.TenantId,
            CardCode = cardCode,
            CardName = bp.CardName,
            BankCode = bp.DefaultBankCode,
            Branch = bp.DefaultBranch,
            AccountNo = bp.DefaultAccount,
            Iban = bp.IBAN,
            AccountsSignature = VendorBankSnapshot.BuildAccountsSignature(bp.BPBankAccounts),
            UpdatedAt = DateTime.UtcNow
        };

        // 2. No baseline (or vendor creation): learn silently, never alert on first sight
        if (snapshot is null || evt.EventType == "Created")
        {
            await _vendorBankRepo.UpsertAsync(newSnapshot, ct);
            await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
            _logger.LogInformation("Vendor bank baseline recorded for {CardCode} (tenant {TenantId})", cardCode, evt.TenantId);
            return;
        }

        // 3. No bank change: discard silently
        if (snapshot.Signature == currentSignature)
        {
            await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
            return;
        }

        // 4. Bank account changed: alert and update the baseline
        var userSign = ExtractUserSign(evt.Payload);
        var userName = userSign is not null
            ? await sapClient.GetUserNameAsync(userSign.Value, ct) ?? $"desconocido (key {userSign})"
            : "desconocido";

        var message =
            $"⚠️ <b>Cambio de cuenta bancaria de proveedor</b>\n" +
            $"Proveedor: {cardCode} - {bp.CardName}\n" +
            $"Cuentas anteriores: {FormatAccountsSignature(snapshot.AccountsSignature)}\n" +
            $"Cuentas nuevas: {FormatAccountsSignature(newSnapshot.AccountsSignature)}\n" +
            $"Usuario SAP: {userName}\n" +
            $"Tenant: {evt.TenantId}";

        var sent = await _telegramNotifier.SendMessageAsync(message, ct);
        if (!sent)
        {
            _logger.LogWarning("Telegram alert for vendor {CardCode} could not be sent (disabled or failed). Change: {Old} -> {New}",
                cardCode, snapshot.Signature, currentSignature);
        }

        await _vendorBankRepo.UpsertAsync(newSnapshot, ct);
        await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
        _logger.LogWarning("Vendor bank account changed for {CardCode} (tenant {TenantId}) by {User}. Alert sent: {Sent}",
            cardCode, evt.TenantId, userName, sent);
    }

    private static int? ExtractUserSign(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.TryGetProperty("userSign", out var el) && el.TryGetInt32(out var userSign))
                return userSign;
        }
        catch (JsonException) { /* malformed payload: user stays unknown */ }
        return null;
    }

    private static string FormatBankAccount(string? bankCode, string? branch, string? accountNo, string? iban)
    {
        var parts = new[] { bankCode, branch, accountNo, iban }
            .Where(p => !string.IsNullOrWhiteSpace(p) && p != "-1")
            .ToArray();
        return parts.Length == 0 ? "(sin cuenta)" : string.Join(" / ", parts);
    }

    /// <summary>
    /// Renders a stored accounts signature ("bank|branch|account|iban;...") as
    /// human-readable "bank / account, ..." for the Telegram message.
    /// </summary>
    private static string FormatAccountsSignature(string? accountsSignature)
    {
        if (string.IsNullOrWhiteSpace(accountsSignature)) return "(sin cuentas)";

        var rows = accountsSignature.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(row =>
            {
                var parts = row.Split('|');
                return FormatBankAccount(
                    parts.ElementAtOrDefault(0),
                    parts.ElementAtOrDefault(1),
                    parts.ElementAtOrDefault(2),
                    parts.ElementAtOrDefault(3));
            });
        return string.Join(", ", rows);
    }

    private async Task HandleDeadLetterAsync(HanaOutboxEvent evt, string correlationId, string errorMessage, long durationMs, ConcurrentBag<IntegrationLog>? pendingLogs, CancellationToken ct)
    {
        _logger.LogWarning("Event {EventId} moved to dead letter due to business error: {Error}", evt.Id, errorMessage);
        await _hanaRepo.MarkDeadLetterAsync(evt.Id, errorMessage, ct);
        await PromoteToDeadLetterAsync(evt, correlationId, errorMessage, durationMs, pendingLogs, ct);

        await _alertingService.RaiseAlertAsync(
            AlertType.DeadLetter,
            AlertSeverity.Critical,
            evt.TenantId,
            "Dead Letter event created",
            $"Event {evt.EventType} ({evt.AggregateId}) moved to DLQ: {errorMessage}",
            $"EventId={evt.Id}, AggregateId={evt.AggregateId}",
            ct);
    }

    private async Task ProcessPriceListAsync(HanaOutboxEvent evt, ICrmConnector crmClient, string correlationId, CancellationToken ct)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<PriceListChangedPayload>(evt.Payload ?? "{}", new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (payload == null)
            {
                _logger.LogWarning("Empty payload for PriceList event {EventId}", evt.Id);
                await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
                return;
            }

            _logger.LogInformation("Sending PriceList ListNum={ListNum} CardCode={CardCode} batch {BatchIndex}/{BatchCount} with {Count} items to CRM",
                payload.ListNum, payload.CardCode, payload.BatchIndex, payload.BatchCount, payload.Items.Count);

            var response = await crmClient.SyncPriceListBatchAsync(payload, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogInformation("PriceList ListNum={ListNum} batch {BatchIndex} already exists in CRM (409). Treating as success.",
                    payload.ListNum, payload.BatchIndex);
            }
            else if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"CRM returned {(int)response.StatusCode}: {response.ErrorMessage}");
            }

            await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
            _logger.LogInformation("PriceList ListNum={ListNum} batch {BatchIndex} processed successfully", payload.ListNum, payload.BatchIndex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process PriceList event {EventId}", evt.Id);
            throw;
        }
    }

    private async Task ProcessPriceListHeaderAsync(HanaOutboxEvent evt, ICrmConnector crmClient, string correlationId, CancellationToken ct)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<PriceListHeaderPayload>(evt.Payload ?? "{}", new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            if (payload == null)
            {
                _logger.LogWarning("Empty payload for PriceListHeader event {EventId}", evt.Id);
                await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
                return;
            }

            _logger.LogInformation("Sending PriceListHeader ListNum={ListNum} Name={ListName} to CRM",
                payload.ListNum, payload.ListName);

            var response = await crmClient.SyncPriceListHeaderAsync(payload, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogInformation("PriceListHeader ListNum={ListNum} already exists in CRM (409). Treating as success.",
                    payload.ListNum);
            }
            else if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"CRM returned {(int)response.StatusCode}: {response.ErrorMessage}");
            }

            await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
            _logger.LogInformation("PriceListHeader ListNum={ListNum} processed successfully", payload.ListNum);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process PriceListHeader event {EventId}", evt.Id);
            throw;
        }
    }

    private async Task ProcessHansaBatchAsync(List<HanaOutboxEvent> events, ConcurrentBag<IntegrationLog>? pendingLogs, CancellationToken ct)
    {
        // Group by tenant + objectType (each batch payload must have a single object type)
        var groups = events.GroupBy(e => new { e.TenantId, e.ObjectType });

        foreach (var group in groups)
        {
            var tenantId = group.Key.TenantId;
            var objectType = group.Key.ObjectType;
            var groupEvents = group.ToList();
            var batchSize = _hansaConfig.Value.BatchSize;
            if (batchSize < 1) batchSize = 25;

            var totalRecords = groupEvents.Count;
            var batchQuantity = (int)Math.Ceiling(totalRecords / (double)batchSize);

            _logger.LogInformation(
                "HansaCRM batch for tenant {TenantId} object {ObjectType}: {TotalRecords} records, {BatchQuantity} batches of max {BatchSize}",
                tenantId, objectType, totalRecords, batchQuantity, batchSize);

            // Resolve clients once per tenant
            var sapClient = await _clientFactory.GetSapClientAsync(tenantId);
            var crmConnector = await _clientFactory.GetCrmConnectorAsync(tenantId);

            if (crmConnector is not HansaCrmConnector hansaConnector)
            {
                _logger.LogWarning("Tenant {TenantId} expected HansaCrm connector but got {ConnectorType}. Falling back to individual processing.", tenantId, crmConnector.GetType().Name);
                foreach (var evt in groupEvents)
                {
                    await ProcessEventAsync(evt, pendingLogs, ct);
                }
                continue;
            }

            for (int batchNumber = 1; batchNumber <= batchQuantity; batchNumber++)
            {
                var subBatch = groupEvents.Skip((batchNumber - 1) * batchSize).Take(batchSize).ToList();
                var batchRecords = subBatch.Count;

                await ProcessHansaSubBatchAsync(
                    subBatch, sapClient, hansaConnector,
                    tenantId, objectType, totalRecords, batchRecords, batchQuantity, batchNumber, pendingLogs, ct);
            }
        }
    }

    private async Task ProcessHansaSubBatchAsync(
        List<HanaOutboxEvent> subBatch,
        ServiceLayerClient sapClient,
        HansaCrmConnector hansaConnector,
        string tenantId,
        string objectType,
        int totalRecords,
        int batchRecords,
        int batchQuantity,
        int batchNumber,
        ConcurrentBag<IntegrationLog>? pendingLogs,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Fetch SAP data for every event in the sub-batch
            var customerPayloads = new List<CrmCustomerPayload>();
            var invoicePayloads = new List<CrmInvoicePayload>();

            foreach (var evt in subBatch)
            {
                switch (objectType)
                {
                    case "2": // BusinessPartners
                        var sapCustomer = await sapClient.GetBusinessPartnerAsync(evt.AggregateId, ct);
                        customerPayloads.Add(CustomerMapper.ToCrmPayload(sapCustomer));
                        break;

                    case "13": // Invoices
                        if (!int.TryParse(evt.AggregateId, out var docEntry))
                            throw new InvalidOperationException($"Invalid DocEntry in AggregateId: {evt.AggregateId}");
                        var sapInvoice = await sapClient.GetInvoiceAsync(docEntry, ct);
                        invoicePayloads.Add(InvoiceMapper.ToCrmPayload(sapInvoice));
                        break;

                    default:
                        _logger.LogWarning("HansaCRM batch does not support object type {ObjectType} for event {EventId}", objectType, evt.Id);
                        await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
                        break;
                }
            }

            // Send batch to HansaCRM
            CrmApiResponse<object> response;
            if (objectType == "2" && customerPayloads.Count > 0)
            {
                response = await hansaConnector.CreateCustomerBatchAsync(
                    customerPayloads, totalRecords, batchRecords, batchQuantity, batchNumber, ct);
            }
            else if (objectType == "13" && invoicePayloads.Count > 0)
            {
                response = await hansaConnector.CreateInvoiceBatchAsync(
                    invoicePayloads, totalRecords, batchRecords, batchQuantity, batchNumber, ct);
            }
            else
            {
                // Nothing to send (unsupported type or empty after filtering)
                return;
            }

            sw.Stop();

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                _logger.LogInformation("HansaCRM batch {BatchNumber}/{BatchQuantity} returned 409 for tenant {TenantId}. Treating as success.", batchNumber, batchQuantity, tenantId);
            }
            else if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"HansaCRM returned {(int)response.StatusCode}: {response.ErrorMessage}");
            }

            // Mark all events in sub-batch as processed
            foreach (var evt in subBatch)
            {
                await _hanaRepo.MarkProcessedAsync(evt.Id, ct);
                IntegrationMetrics.RecordEventProcessed(evt.EventType, tenantId, "success");
                IntegrationMetrics.RecordEventLatency(evt.EventType, tenantId, sw.ElapsedMilliseconds);
                await WriteLogAsync(new IntegrationLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    CorrelationId = correlationId,
                    Direction = IntegrationDirection.SapToCrm,
                    EventType = evt.EventType,
                    ExternalId = evt.AggregateId,
                    Status = "success",
                    DurationMs = sw.ElapsedMilliseconds,
                    CreatedAt = DateTime.UtcNow
                }, pendingLogs, ct);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "HansaCRM batch {BatchNumber}/{BatchQuantity} failed for tenant {TenantId}", batchNumber, batchQuantity, tenantId);

            if (IsCircuitBreakerException(ex))
            {
                IntegrationMetrics.RecordCircuitBreakerChange("crm", tenantId, "open");
                await _alertingService.RaiseAlertAsync(
                    AlertType.CircuitBreaker,
                    AlertSeverity.Critical,
                    tenantId,
                    "Circuit breaker opened",
                    $"Circuit breaker detected for HansaCRM batch: {ex.Message}",
                    $"Batch={batchNumber}/{batchQuantity}",
                    ct);
            }

            // Mark all events in sub-batch as failed
            foreach (var evt in subBatch)
            {
                var currentAttempt = evt.AttemptCount + 1;
                await _hanaRepo.MarkFailedAsync(evt.Id, ex.Message, ct);
                IntegrationMetrics.RecordEventProcessed(evt.EventType, tenantId, "error");
                IntegrationMetrics.RecordRetry(evt.EventType, tenantId, currentAttempt);
                IntegrationMetrics.RecordEventLatency(evt.EventType, tenantId, sw.ElapsedMilliseconds);

                if (evt.AttemptCount + 1 >= _config.Value.MaxAttempts)
                {
                    await PromoteToDeadLetterAsync(evt, correlationId, ex.Message, sw.ElapsedMilliseconds, pendingLogs, ct);
                }

                await WriteLogAsync(new IntegrationLog
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    CorrelationId = correlationId,
                    Direction = IntegrationDirection.SapToCrm,
                    EventType = evt.EventType,
                    ExternalId = evt.AggregateId,
                    Status = "error",
                    ErrorMessage = ex.Message,
                    DurationMs = sw.ElapsedMilliseconds,
                    CreatedAt = DateTime.UtcNow
                }, pendingLogs, ct);
            }
        }
    }

    private static bool IsCircuitBreakerException(Exception ex)
    {
        if (ex is BrokenCircuitException)
            return true;

        if (ex.InnerException is not null)
            return IsCircuitBreakerException(ex.InnerException);

        return false;
    }

    internal async Task<bool> IsQuotaExceededAsync(string tenantId, TenantQuota quota, CancellationToken ct)
    {
        if (quota.MaxEventsPerHour <= 0)
            return false;

        var since = DateTime.UtcNow.AddHours(-1);
        var count = await _logRepo.GetProcessedCountByTenantAsync(tenantId, since, ct);
        return count >= quota.MaxEventsPerHour;
    }

    private async Task PromoteToDeadLetterAsync(HanaOutboxEvent evt, string correlationId, string errorMessage, long durationMs, ConcurrentBag<IntegrationLog>? pendingLogs, CancellationToken ct)
    {
        IntegrationMetrics.RecordDeadLetter(evt.EventType, evt.TenantId, "max_retries_exceeded");
        await _deadLetterRepo.AddAsync(new DeadLetterEvent
        {
            Id = Guid.NewGuid(),
            TenantId = evt.TenantId,
            Source = "hana_outbox",
            EventType = evt.EventType,
            AggregateId = evt.AggregateId,
            Payload = evt.AggregateId, // In production, serialize the full payload
            ErrorMessage = errorMessage,
            AttemptCount = evt.AttemptCount,
            OccurredAt = DateTime.SpecifyKind(evt.OccurredAt, DateTimeKind.Utc),
            DeadLetteredAt = DateTime.UtcNow
        }, ct);

        await WriteLogAsync(new IntegrationLog
        {
            Id = Guid.NewGuid(),
            TenantId = evt.TenantId,
            CorrelationId = correlationId,
            Direction = IntegrationDirection.SapToCrm,
            EventType = evt.EventType,
            ExternalId = evt.AggregateId,
            Status = "dead_letter",
            ErrorMessage = errorMessage,
            DurationMs = durationMs,
            CreatedAt = DateTime.UtcNow
        }, pendingLogs, ct);
    }
}
