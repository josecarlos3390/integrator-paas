namespace Integration.Shared.Connectors.HansaCrm;

/// <summary>
/// Default values for HansaCRM fields that are not available in SAP BusinessPartner.
/// </summary>
public class HansaCrmDefaults
{
    public string Organization { get; set; } = string.Empty;
    public List<string> SalesSectors { get; set; } = new();
    public List<string> SalesChannels { get; set; } = new();
    public List<string> SalesOffices { get; set; } = new();
    public List<string> Warehouses { get; set; } = new();
}
