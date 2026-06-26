namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Configuration options for the NIBSS mCash/USSD adapter.
/// Bound from the "MCash" configuration section.
/// </summary>
public class MCashOptions
{
    public const string SectionName = "MCash";

    /// <summary>
    /// Base URL for the NIBSS mCash API.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API key for authenticating with NIBSS mCash.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// USSD session timeout in milliseconds. If the session does not complete
    /// within this period, the adapter marks the transaction as timed-out and
    /// initiates a status inquiry with NIBSS.
    /// </summary>
    public int SessionTimeoutMs { get; set; } = 60_000;
}
