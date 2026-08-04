using System.Net.Http.Json;
using Integration.Shared.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Integration.Shared.Services;

/// <summary>
/// Telegram Bot API notifier. Fire-and-forget friendly: never throws,
/// failures are logged and reported as false so they do not block
/// event processing (same philosophy as the alerting webhook).
/// </summary>
public class TelegramNotifier : ITelegramNotifier
{
    private readonly HttpClient _httpClient;
    private readonly TelegramConfig _config;
    private readonly ILogger<TelegramNotifier> _logger;

    public TelegramNotifier(
        HttpClient httpClient,
        IOptions<TelegramConfig> config,
        ILogger<TelegramNotifier> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
    }

    public async Task<bool> SendMessageAsync(string message, CancellationToken ct = default)
    {
        if (!_config.Enabled)
        {
            _logger.LogDebug("Telegram notifications disabled. Message not sent.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(_config.BotToken) || string.IsNullOrWhiteSpace(_config.ChatId))
        {
            _logger.LogWarning("Telegram is enabled but BotToken/ChatId are not configured. Message not sent.");
            return false;
        }

        try
        {
            var url = $"https://api.telegram.org/bot{_config.BotToken}/sendMessage";
            var payload = new
            {
                chat_id = _config.ChatId,
                text = message,
                parse_mode = "HTML",
                disable_web_page_preview = true
            };

            var response = await _httpClient.PostAsJsonAsync(url, payload, ct);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Telegram sendMessage failed with {StatusCode}: {Body}", (int)response.StatusCode, body);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send Telegram notification");
            return false;
        }
    }
}
