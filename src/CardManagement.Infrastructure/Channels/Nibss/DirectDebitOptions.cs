namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Configuration options for the NIBSS Direct Debit adapter.
/// Bound from the "DirectDebit" configuration section.
/// </summary>
public class DirectDebitOptions
{
    public const string SectionName = "DirectDebit";

    /// <summary>
    /// Base URL for the NIBSS Direct Debit API.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API key for authenticating with NIBSS Direct Debit.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of retry attempts for failed debits.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Interval in hours between retry attempts for failed debits.
    /// </summary>
    public int RetryIntervalHours { get; set; } = 24;
}
