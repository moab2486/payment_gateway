namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Configuration options for the NIBSS GAPS (bulk/batch payment) adapter.
/// </summary>
public class GapsOptions
{
    /// <summary>
    /// Base URL for the NIBSS GAPS API.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API key for authenticating with the NIBSS GAPS service.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Maximum number of items allowed in a single batch submission.
    /// Default: 1000.
    /// </summary>
    public int MaxBatchSize { get; set; } = 1000;

    /// <summary>
    /// Interval in milliseconds for polling batch processing status.
    /// Default: 5000ms.
    /// </summary>
    public int StatusPollIntervalMs { get; set; } = 5000;
}
