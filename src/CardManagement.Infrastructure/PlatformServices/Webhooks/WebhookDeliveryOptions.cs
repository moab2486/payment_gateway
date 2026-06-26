namespace CardManagement.Infrastructure.PlatformServices.Webhooks;

/// <summary>
/// Configuration options for webhook delivery behavior.
/// </summary>
public sealed class WebhookDeliveryOptions
{
    public const string SectionName = "WebhookDelivery";

    /// <summary>
    /// Base delay in milliseconds for exponential backoff retry.
    /// </summary>
    public double BaseDelayMs { get; set; } = 5000;

    /// <summary>
    /// Multiplier for exponential backoff calculation.
    /// </summary>
    public double BackoffMultiplier { get; set; } = 2.0;

    /// <summary>
    /// Maximum number of retry attempts before moving to DLQ.
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// HTTP timeout in seconds for webhook delivery requests.
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Number of consecutive failures before a subscription is suspended.
    /// </summary>
    public int SuspensionThreshold { get; set; } = 10;

    /// <summary>
    /// Concurrency setting for the webhook delivery worker pool.
    /// </summary>
    public int Concurrency { get; set; } = 16;
}
