namespace CardManagement.Infrastructure.Saga;

/// <summary>
/// Configuration options for saga orchestration behavior.
/// </summary>
public sealed class SagaOptions
{
    /// <summary>
    /// Maximum number of retries for a failed compensation action before flagging for manual intervention.
    /// </summary>
    public int MaxCompensationRetries { get; set; } = 5;

    /// <summary>
    /// Initial delay in milliseconds for exponential backoff on compensation retries.
    /// Subsequent delays double: 1000, 2000, 4000, 8000, ...
    /// </summary>
    public int InitialRetryDelayMs { get; set; } = 1000;
}
