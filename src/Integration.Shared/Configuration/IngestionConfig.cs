namespace Integration.Shared.Configuration;

/// <summary>
/// Configuration for the Data Ingestor worker.
/// </summary>
public class IngestionConfig
{
    public int PollingSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
    public int MaxConcurrency { get; set; } = 5;
    public int MaxAttempts { get; set; } = 3;
    public int RetryBaseDelaySeconds { get; set; } = 5;
    public int RetryBackoffMultiplier { get; set; } = 2;
}
