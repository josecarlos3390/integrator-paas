namespace Integration.Shared.Services;

/// <summary>
/// Sends operational notifications through the Telegram Bot API.
/// </summary>
public interface ITelegramNotifier
{
    /// <summary>
    /// Sends a text message to the configured chat.
    /// Returns true when Telegram accepted the message.
    /// </summary>
    Task<bool> SendMessageAsync(string message, CancellationToken ct = default);
}
