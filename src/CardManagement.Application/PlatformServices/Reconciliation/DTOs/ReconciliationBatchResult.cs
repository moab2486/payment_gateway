namespace CardManagement.Application.PlatformServices.Reconciliation.DTOs;

/// <summary>
/// Result of executing a reconciliation batch matching operation.
/// </summary>
public record ReconciliationBatchResult
{
    /// <summary>
    /// The batch ID that was processed.
    /// </summary>
    public required Guid BatchId { get; init; }

    /// <summary>
    /// Total settlement line items processed.
    /// </summary>
    public required int TotalItems { get; init; }

    /// <summary>
    /// Number of items that matched successfully.
    /// </summary>
    public required int MatchedCount { get; init; }

    /// <summary>
    /// Number of exceptions identified (mismatches and unmatched items).
    /// </summary>
    public required int ExceptionCount { get; init; }

    /// <summary>
    /// Number of exceptions that were auto-resolved by rule engine.
    /// </summary>
    public required int AutoResolvedCount { get; init; }

    /// <summary>
    /// Whether the batch completed successfully.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if the batch failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
