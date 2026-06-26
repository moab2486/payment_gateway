namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Configuration options for the NIBSS e-BillsPay adapter.
/// Bound from the "EBillsPay" configuration section.
/// </summary>
public class EBillsPayOptions
{
    public const string SectionName = "EBillsPay";

    /// <summary>
    /// Base URL for the NIBSS e-BillsPay API.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API key for authenticating with NIBSS e-BillsPay.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Cache TTL in minutes for the biller directory. Defaults to 60 minutes.
    /// </summary>
    public int BillerDirectoryCacheTtlMinutes { get; set; } = 60;
}
