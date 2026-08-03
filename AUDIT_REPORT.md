# Code Audit Report — Integration Bus

**Date:** 2026-05-18  
**Scope:** `src/Integration.Api`, `src/Integration.Worker`, `src/Integration.Shared`, `src/Integration.Shared.Tests`  
**Tests Status:** 34/34 passing, 0 warnings, 0 compilation errors (except Worker blocked by running process)

---

## 🔴 CRITICAL

### 1. `CrmWebhookController` inyecta `ServiceLayerClient` que NO está registrado en DI
- **File:** `src/Integration.Api/Controllers/CrmWebhookController.cs:24,32`
- **Severity:** CRITICAL
- **Problem:** `ServiceLayerClient` is injected directly but never registered in `Program.cs`. Any request to `POST api/crm/orders` will throw `InvalidOperationException: Unable to resolve service for type 'ServiceLayerClient'` at runtime.
- **Fix:** Inject `ITenantClientFactory` instead and resolve the client via `await _clientFactory.GetSapClientAsync(tenantId)`.

### 2. `ServerCertificateCustomValidationCallback` bypasses ALL SSL validation
- **File:** `src/Integration.Shared/Clients/TenantClientFactory.cs:94,132,158`
- **Severity:** CRITICAL
- **Problem:** `ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true` disables certificate validation for SAP, Mock CRM, and HansaCRM. This makes the application vulnerable to MITM attacks in production.
- **Fix:** Make this configurable via `appsettings.json` (e.g., `ValidateCertificates: false` for dev only). Default must be `true` in production.

### 3. Outbox events have NO distributed locking — duplicate processing risk
- **File:** `src/Integration.Shared/Repositories/HanaOutboxRepository.cs:32-61`
- **Severity:** CRITICAL
- **Problem:** `FetchPendingAsync` reads events `WHERE PROCESSED_AT IS NULL` but does NOT acquire a lease. If multiple Worker instances run (horizontal scaling), they will fetch and process the same events concurrently, causing:
  - Duplicate CRM inserts
  - Race conditions on `ATTEMPT_COUNT` increments
  - Conflicting idempotency checks
- **Fix:** Add a `leased_until` timestamp column. Update with `WHERE id = ? AND (leased_until IS NULL OR leased_until < NOW())` before processing. Or use `SELECT FOR UPDATE` if HANA supports it.

### 4. `ATTEMPT_COUNT < 5` is hardcoded but `MaxAttempts` is configurable
- **File:** `src/Integration.Shared/Repositories/HanaOutboxRepository.cs:54`
- **Severity:** CRITICAL
- **Problem:** The SQL query hardcodes `ATTEMPT_COUNT < 5`, but `OutboxConfig.MaxAttempts` may be set to a different value (e.g., 3 or 10). Events beyond `MaxAttempts` but under 5 will keep being fetched and retried indefinitely.
- **Fix:** Pass `maxAttempts` as a parameter to `FetchPendingAsync` and use it in the SQL query.

---

## 🟠 HIGH

### 5. `TenantClientFactory` creates `new HttpClient()` manually — socket exhaustion risk
- **File:** `src/Integration.Shared/Clients/TenantClientFactory.cs:90-109,130-148,156-170`
- **Severity:** HIGH
- **Problem:** `new HttpClient(handler)` is created per tenant and cached in `ConcurrentDictionary`. While caching mitigates the issue, if tenants are dynamically added/removed, old `HttpClient` instances are never disposed and their underlying `HttpClientHandler`/`SocketsHttpHandler` keeps sockets open. Also, no `Timeout` is configured (defaults to 100s).
- **Fix:** Use `IHttpClientFactory` with named clients for each system. At minimum, set `httpClient.Timeout = TimeSpan.FromSeconds(30)`.

### 6. `HansaCrmAuthService` and `HansaCrmClient` share the same `HttpClient`
- **File:** `src/Integration.Shared/Clients/TenantClientFactory.cs:166-179`
- **Severity:** HIGH
- **Problem:** Both services receive the same `HttpClient` instance. If `AuthService` modifies headers (e.g., `Content-Type` for form-urlencoded) or if `HansaCrmClient` adds `Authorization`, they can interfere with each other due to shared `DefaultRequestHeaders`.
- **Fix:** Create separate `HttpClient` instances for auth and API calls, or use `HttpRequestMessage` per-request instead of mutating `DefaultRequestHeaders`.

### 7. Circuit breaker detection uses fragile string matching
- **File:** `src/Integration.Worker/Dispatchers/HanaOutboxDispatcher.cs:201`
- **Severity:** HIGH
- **Problem:** `ex.Message.Contains("circuit", StringComparison.OrdinalIgnoreCase)` will break if Polly changes exception messages. It also misses `BrokenCircuitException` wrapped in `AggregateException`.
- **Fix:** Check exception type: `ex is BrokenCircuitException || (ex.InnerException is BrokenCircuitException)`.

### 8. `_cycleCts` is not thread-safe in `OutboxDispatcherWorker`
- **File:** `src/Integration.Worker/Workers/OutboxDispatcherWorker.cs:21,49,93`
- **Severity:** HIGH
- **Problem:** `_cycleCts` is a mutable field written by `ExecuteAsync` and read by `StopAsync` (different threads). A race condition can cause `StopAsync` to cancel a null or stale CTS, or miss the current one.
- **Fix:** Use `Interlocked.Exchange` or a `lock` to synchronize access. Or use `CancellationTokenSource.CreateLinkedTokenSource(stoppingToken)` inline without storing it in a field.

---

## 🟡 MEDIUM

### 9. Magic strings for object types scattered across the codebase
- **Files:** `HanaOutboxDispatcher.cs:122,125,128,131`, `TenantFeatureService.cs:22-25`, `IntegrationConfig.cs:39-42`
- **Severity:** MEDIUM
- **Problem:** `"2"`, `"13"`, `"17"`, `"PRICE_LIST"`, `"PRICE_LIST_HEADER"` are magic strings. Typos or changes require updating multiple files.
- **Fix:** Create a `SapObjectType` static class or enum with named constants.

### 10. Hardcoded defaults in `HansaCrmMapper`
- **File:** `src/Integration.Shared/Connectors/HansaCrm/Mappers/HansaCrmMapper.cs`
- **Severity:** MEDIUM
- **Problem:** Values like `"02"` (price group), `"1"` (invent_taking), `"DS"` (class_document), `"00000000"`, `"EXT_HANSA"`, `"Regimen General"` are hardcoded.
- **Fix:** Move to `HansaCrmDefaults` or `HansaCrmConfig` so they can be configured per tenant.

### 11. `MockCrmController` accepts `object` type — loses model validation
- **File:** `src/Integration.Api/Controllers/MockCrmController.cs:26,54`
- **Severity:** MEDIUM
- **Problem:** `CreateInvoice([FromBody] object payload)` and `OrderResult([FromBody] object payload)` bypass ASP.NET Core model validation entirely.
- **Fix:** Use strongly-typed DTOs.

### 12. RabbitMQ fallback credentials are hardcoded
- **File:** `src/Integration.Api/Program.cs:99-100`, `src/Integration.Worker/Program.cs:104-105`
- **Severity:** MEDIUM
- **Problem:** Fallback values `"guest"` / `"guest"` are the RabbitMQ default credentials. If configuration is missing, the app connects with insecure defaults.
- **Fix:** Remove fallback values. Throw an explicit exception if RabbitMQ credentials are not configured.

### 13. Missing `[ProducesResponseType]` on API controllers
- **File:** `src/Integration.Api/Controllers/*`
- **Severity:** MEDIUM
- **Problem:** Controllers don't document response types for Swagger/OpenAPI. This makes API contract discovery harder for consumers.
- **Fix:** Add `[ProducesResponseType(typeof(...), StatusCodes.Status200OK)]` attributes.

### 14. `TenantClientFactory.GetTenantConfigAsync` swallows all exceptions equally
- **File:** `src/Integration.Shared/Clients/TenantClientFactory.cs:201-205`
- **Severity:** MEDIUM
- **Problem:** A network error connecting to PostgreSQL and a "tenant not found" both result in the same warning log and fallback to global config. This masks real DB issues.
- **Fix:** Distinguish between `DbException` (log error, throw) and `null result` (log info, use fallback).

---

## 🟢 LOW

### 15. `AdminController` has an unaddressed TODO
- **File:** `src/Integration.Api/Controllers/AdminController.cs:67`
- **Severity:** LOW
- **Problem:** `// TODO: re-enqueue in HANA or RabbitMQ depending on the source`
- **Fix:** Implement or create a tracking issue.

### 16. `HansaCrmConnector` has NotImplementedException for price lists
- **File:** `src/Integration.Shared/Connectors/HansaCrm/HansaCrmConnector.cs:42,48`
- **Severity:** LOW
- **Problem:** Expected — these are placeholders awaiting payload definitions from HansaCRM team.

### 17. `OutboxDispatcherWorker.StopAsync` uses `Task.WhenAny` without observing exceptions
- **File:** `src/Integration.Worker/Workers/OutboxDispatcherWorker.cs:89`
- **Severity:** LOW
- **Problem:** If `shutdownTask` fails, the exception is lost because `Task.WhenAny` returns the completed task but exceptions are not observed until `await`.
- **Fix:** `await completed;` or use `await Task.WhenAny(...).ContinueWith(...)`.

---

## ✅ Positive Findings

| Finding | Evidence |
|---|---|
| No `async void` methods | Grep returned 0 matches |
| No `.Result` / `.Wait()` blocking | Grep returned 0 matches |
| No `DateTime.Now` / `DateTime.Today` | All timestamps use `DateTime.UtcNow` |
| No SQL injection in Dapper | All queries use parameterized statements |
| EF Core read queries use `AsNoTracking()` | Consistent across repositories |
| `CancellationToken` propagated in most places | Only 4 missing in dispatcher (fixed today) |
| Tests cover critical mappers | Customer, Invoice, Order, HansaCrm mappers all tested |
| Circuit breakers are isolated | SAP and CRM have separate Polly pipelines |
| Graceful shutdown implemented | 30s timeout with forced cancellation |

---

## Summary by Severity

| Severity | Count |
|---|---|
| 🔴 CRITICAL | 4 |
| 🟠 HIGH | 4 |
| 🟡 MEDIUM | 6 |
| 🟢 LOW | 3 |
| ✅ Positive | 9 |

**Recommendation:** Address CRITICAL items before any production deployment. Items #1, #2, and #3 are runtime failures or security vulnerabilities.
