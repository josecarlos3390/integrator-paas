using System.Net;

namespace Integration.Shared.Connectors;

/// <summary>
/// Generic CRM API response decoupled from Refit.
/// Used by all CRM connectors so the dispatcher can inspect status codes uniformly.
/// </summary>
public class CrmApiResponse<T>
{
    public HttpStatusCode StatusCode { get; set; }
    public bool IsSuccessStatusCode => (int)StatusCode >= 200 && (int)StatusCode <= 299;
    public T? Content { get; set; }
    public string? ErrorMessage { get; set; }
}
