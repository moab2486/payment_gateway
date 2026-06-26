namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Result of a GAPS batch status inquiry, containing per-item processing statuses.
/// </summary>
public sealed class GapsBatchStatusResult
{
    /// <summary>
    /// Whether the status inquiry itself was successful.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// The batch reference that was queried.
    /// </summary>
    public string BatchReference { get; }

    /// <summary>
    /// Per-item processing statuses (successful, failed, or pending).
    /// </summary>
    public IReadOnlyList<GapsBatchItemStatus> ItemStatuses { get; }

    /// <summary>
    /// Error code if the inquiry failed (null on success).
    /// </summary>
    public string? ErrorCode { get; }

    /// <summary>
    /// Error message if the inquiry failed (null on success).
    /// </summary>
    public string? ErrorMessage { get; }

    private GapsBatchStatusResult(
        bool success,
        string batchReference,
        IReadOnlyList<GapsBatchItemStatus> itemStatuses,
        string? errorCode,
        string? errorMessage)
    {
        Success = success;
        BatchReference = batchReference;
        ItemStatuses = itemStatuses;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// Creates a successful batch status result with per-item statuses.
    /// </summary>
    public static GapsBatchStatusResult Successful(
        string batchReference,
        IReadOnlyList<GapsBatchItemStatus> itemStatuses)
    {
        return new GapsBatchStatusResult(true, batchReference, itemStatuses, null, null);
    }

    /// <summary>
    /// Creates a failed batch status result with an error code and message.
    /// </summary>
    public static GapsBatchStatusResult Failure(
        string batchReference,
        string errorCode,
        string errorMessage)
    {
        return new GapsBatchStatusResult(false, batchReference, Array.Empty<GapsBatchItemStatus>(), errorCode, errorMessage);
    }
}
