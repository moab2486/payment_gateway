namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Represents the outcome of a scheduled debit attempt that accounts for retry logic.
/// </summary>
public record DebitRetryResult
{
    /// <summary>
    /// The debit was successfully processed by NIBSS.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Indicates whether the debit needs to be retried (insufficient funds, within retry limits).
    /// </summary>
    public bool NeedsRetry { get; init; }

    /// <summary>
    /// Indicates that all retry attempts have been exhausted without success.
    /// </summary>
    public bool RetriesExhausted { get; init; }

    /// <summary>
    /// The current retry attempt count (0 for the first attempt).
    /// </summary>
    public int RetryCount { get; init; }

    /// <summary>
    /// The scheduled time for the next retry attempt, if applicable.
    /// </summary>
    public DateTime? NextRetryTime { get; init; }

    /// <summary>
    /// The NIBSS reference returned on success.
    /// </summary>
    public string? NibssReference { get; init; }

    /// <summary>
    /// The mandate ID associated with this debit.
    /// </summary>
    public Guid? MandateId { get; init; }

    /// <summary>
    /// Error code from NIBSS if the debit failed.
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// Error message from NIBSS if the debit failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    public static DebitRetryResult Successful(Guid mandateId, string? nibssReference) =>
        new()
        {
            IsSuccess = true,
            NeedsRetry = false,
            RetriesExhausted = false,
            RetryCount = 0,
            MandateId = mandateId,
            NibssReference = nibssReference
        };

    public static DebitRetryResult ScheduledForRetry(
        Guid mandateId,
        int retryCount,
        DateTime nextRetryTime,
        string errorCode,
        string errorMessage) =>
        new()
        {
            IsSuccess = false,
            NeedsRetry = true,
            RetriesExhausted = false,
            RetryCount = retryCount,
            NextRetryTime = nextRetryTime,
            MandateId = mandateId,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

    public static DebitRetryResult Exhausted(
        Guid mandateId,
        int retryCount,
        string errorCode,
        string errorMessage) =>
        new()
        {
            IsSuccess = false,
            NeedsRetry = false,
            RetriesExhausted = true,
            RetryCount = retryCount,
            MandateId = mandateId,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };

    public static DebitRetryResult Failed(string errorCode, string errorMessage) =>
        new()
        {
            IsSuccess = false,
            NeedsRetry = false,
            RetriesExhausted = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage
        };
}
