namespace Integration.Shared.Exceptions;

/// <summary>
/// Exception thrown when the SAP Service Layer returns an error
/// business (4xx) or technical (5xx) that cannot be handled silently.
/// </summary>
public class SapIntegrationException : Exception
{
    public int? SapErrorCode { get; }
    public bool IsBusinessError { get; }

    public SapIntegrationException(string message, int? sapErrorCode = null, bool isBusinessError = false)
        : base(message)
    {
        SapErrorCode = sapErrorCode;
        IsBusinessError = isBusinessError;
    }

    public SapIntegrationException(string message, Exception inner)
        : base(message, inner)
    {
    }
}
