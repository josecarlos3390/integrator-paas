using Integration.Shared.Configuration;
using Integration.Shared.Domain;
using Integration.Shared.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;

namespace Integration.Shared.Services;

/// <summary>
/// Alerting service implementation with deduplication and optional webhook delivery.
/// </summary>
public class AlertingService : IAlertingService
{
    private readonly AlertRepository _alertRepo;
    private readonly IOptions<AlertingConfig> _config;
    private readonly ILogger<AlertingService> _logger;
    private readonly HttpClient _httpClient;
    private static readonly TimeSpan DeduplicationWindow = TimeSpan.FromMinutes(30);

    public AlertingService(
        AlertRepository alertRepo,
        IOptions<AlertingConfig> config,
        ILogger<AlertingService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _alertRepo = alertRepo;
        _config = config;
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(10);
    }

    public async Task RaiseAlertAsync(
        AlertType alertType,
        AlertSeverity severity,
        string tenantId,
        string title,
        string message,
        string? details = null,
        CancellationToken ct = default)
    {
        if (!_config.Value.Enabled)
        {
            _logger.LogDebug("Alerting is disabled. Skipping alert: {Title}", title);
            return;
        }

        try
        {
            var hasRecent = await _alertRepo.HasRecentAlertAsync(tenantId, alertType, DeduplicationWindow, ct);
            if (hasRecent)
            {
                _logger.LogDebug("Deduplicating alert {AlertType} for tenant {TenantId}", alertType, tenantId);
                return;
            }

            var alert = new IntegrationAlert
            {
                Id = Guid.NewGuid(),
                AlertType = alertType,
                Severity = severity,
                TenantId = tenantId,
                Title = title,
                Message = message,
                Details = details,
                IsAcknowledged = false,
                CreatedAt = DateTime.UtcNow
            };

            await _alertRepo.AddAsync(alert, ct);
            _logger.LogWarning("Alert raised: [{Severity}] {Title} for tenant {TenantId}", severity, title, tenantId);

            _ = Task.Run(async () => await SendWebhookAsync(alert, ct), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to raise alert: {Title}", title);
        }
    }

    public async Task AcknowledgeAlertAsync(Guid alertId, string? acknowledgedBy, CancellationToken ct = default)
    {
        await _alertRepo.AcknowledgeAsync(alertId, acknowledgedBy, ct);
        _logger.LogInformation("Alert {AlertId} acknowledged by {AcknowledgedBy}", alertId, acknowledgedBy);
    }

    public Task<(IReadOnlyList<IntegrationAlert> Items, int TotalCount)> GetActiveAlertsAsync(string? tenantId = null, int skip = 0, int take = 25, CancellationToken ct = default)
        => _alertRepo.GetActiveAsync(tenantId, skip: skip, take: take, ct: ct);

    public Task<(IReadOnlyList<IntegrationAlert> Items, int TotalCount)> GetRecentAlertsAsync(string? tenantId = null, int skip = 0, int take = 25, CancellationToken ct = default)
        => _alertRepo.GetRecentAsync(tenantId, skip, take, ct);

    public Task<object> GetStatsAsync(string? tenantId = null, CancellationToken ct = default)
        => _alertRepo.GetStatsAsync(tenantId, ct);

    private async Task SendWebhookAsync(IntegrationAlert alert, CancellationToken ct = default)
    {
        var webhookUrl = _config.Value.WebhookUrl;
        if (string.IsNullOrWhiteSpace(webhookUrl)) return;

        try
        {
            var payload = new
            {
                alert.Id,
                alert.AlertType,
                alert.Severity,
                alert.TenantId,
                alert.Title,
                alert.Message,
                alert.CreatedAt
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(webhookUrl, content, ct);
            _logger.LogDebug("Webhook sent for alert {AlertId}. Status: {StatusCode}", alert.Id, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send webhook for alert {AlertId}", alert.Id);
        }
    }
}
