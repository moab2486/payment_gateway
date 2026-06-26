namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Internal abstraction over NIBSS GAPS (bulk/batch payment) API calls for testability.
/// </summary>
public interface INibssGapsClient
{
    /// <summary>
    /// Submits a batch of payment items to NIBSS GAPS for processing.
    /// </summary>
    Task<GapsBatchResponse> SubmitBatchAsync(GapsBatchRequest request, CancellationToken ct);

    /// <summary>
    /// Queries the processing status of a previously submitted batch.
    /// </summary>
    Task<GapsBatchStatusResponse> QueryBatchStatusAsync(string batchReference, CancellationToken ct);
}

/// <summary>
/// Represents a single item within a GAPS batch payment.
/// </summary>
/// <param name="RecipientAccount">The recipient's bank account number.</param>
/// <param name="BankCode">The recipient's bank code (CBN code).</param>
/// <param name="Amount">Payment amount in minor currency units (kobo).</param>
/// <param name="Narration">Transaction narration/description.</param>
/// <param name="ItemReference">Unique reference for this batch item.</param>
public record GapsBatchItem(
    string RecipientAccount,
    string BankCode,
    decimal Amount,
    string Narration,
    string ItemReference);

/// <summary>
/// Represents a batch payment submission request to NIBSS GAPS.
/// </summary>
/// <param name="BatchReference">Unique reference for the entire batch.</param>
/// <param name="Items">The list of payment items in the batch.</param>
/// <param name="TransactionReference">The originating transaction reference.</param>
public record GapsBatchRequest(
    string BatchReference,
    IReadOnlyList<GapsBatchItem> Items,
    string TransactionReference);

/// <summary>
/// Represents a per-item error returned from NIBSS GAPS.
/// </summary>
/// <param name="ItemReference">Reference of the failed item.</param>
/// <param name="ErrorCode">NIBSS error code for this item.</param>
/// <param name="ErrorMessage">Human-readable error description.</param>
public record GapsBatchItemError(
    string ItemReference,
    string ErrorCode,
    string ErrorMessage);

/// <summary>
/// Represents the response from NIBSS GAPS after batch submission.
/// </summary>
/// <param name="ResponseCode">NIBSS response code (e.g., "00" for success).</param>
/// <param name="ResponseMessage">Human-readable response message.</param>
/// <param name="NibssReference">NIBSS-assigned reference for the batch.</param>
/// <param name="PerItemErrors">Per-item errors if any items were rejected by NIBSS.</param>
public record GapsBatchResponse(
    string ResponseCode,
    string? ResponseMessage,
    string? NibssReference,
    IReadOnlyList<GapsBatchItemError>? PerItemErrors);

/// <summary>
/// Represents the status of an individual item within a processed batch.
/// </summary>
/// <param name="ItemReference">Reference of the batch item.</param>
/// <param name="Status">Processing status: "successful", "failed", or "pending".</param>
/// <param name="ErrorMessage">Error message if the item failed, null otherwise.</param>
public record GapsBatchItemStatus(
    string ItemReference,
    string Status,
    string? ErrorMessage);

/// <summary>
/// Represents the batch status inquiry response from NIBSS GAPS.
/// </summary>
/// <param name="ResponseCode">NIBSS response code for the status inquiry.</param>
/// <param name="BatchReference">The batch reference queried.</param>
/// <param name="ItemStatuses">Per-item processing statuses.</param>
public record GapsBatchStatusResponse(
    string ResponseCode,
    string BatchReference,
    IReadOnlyList<GapsBatchItemStatus> ItemStatuses);
