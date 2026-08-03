using System.Text.Json.Serialization;

namespace Integration.Shared.Dtos;

/// <summary>
/// Result sent back to the caller via callback URL after processing.
/// </summary>
public class IngestionResult
{
    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("correlation_id")]
    public string CorrelationId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("external_id")]
    public string? ExternalId { get; set; }

    [JsonPropertyName("target_system_id")]
    public string? TargetSystemId { get; set; }

    [JsonPropertyName("error_message")]
    public string? ErrorMessage { get; set; }

    [JsonPropertyName("processed_at")]
    public DateTime? ProcessedAt { get; set; }
}
