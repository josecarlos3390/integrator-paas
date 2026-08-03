using System.Text.Json;
using System.Text.Json.Serialization;

namespace Integration.Shared.Dtos;

/// <summary>
/// Root payload for the Data Ingestor endpoint.
/// Inspired by HansaCRM's data-ingestion-services structure.
/// </summary>
public class IngestionPayload
{
    [JsonPropertyName("object")]
    public string Object { get; set; } = string.Empty;

    public IngestionEntry Entry { get; set; } = new();
}

public class IngestionEntry
{
    public string Id { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public IngestionMetadata Metadata { get; set; } = new();
    public IngestionContext Context { get; set; } = new();
    public List<JsonElement> Messages { get; set; } = new();
}

public class IngestionMetadata
{
    [JsonPropertyName("batch_id")]
    public string BatchId { get; set; } = string.Empty;

    [JsonPropertyName("total_records")]
    public int TotalRecords { get; set; }

    [JsonPropertyName("batch_number")]
    public int BatchNumber { get; set; }

    [JsonPropertyName("source_system")]
    public string SourceSystem { get; set; } = string.Empty;

    [JsonPropertyName("target_system")]
    public string TargetSystem { get; set; } = string.Empty;
}

public class IngestionContext
{
    [JsonPropertyName("tenant_id")]
    public string TenantId { get; set; } = string.Empty;

    public string Profile { get; set; } = string.Empty;

    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }
}
