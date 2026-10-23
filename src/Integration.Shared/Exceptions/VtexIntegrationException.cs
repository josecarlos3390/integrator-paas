namespace Integration.Shared.Exceptions;

/// <summary>
/// Upstream VTEX error with the HTTP status so controllers can map 404/401/5xx.
/// </summary>
public class VtexIntegrationException : Exception
{
    public int StatusCode { get; }
    public string? Body { get; }

    public VtexIntegrationException(int statusCode, string? body)
        : base($"VTEX {statusCode}: {body}")
    {
        StatusCode = statusCode;
        Body = body;
    }
}
