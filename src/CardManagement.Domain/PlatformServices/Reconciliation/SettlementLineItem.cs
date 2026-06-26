using CardManagement.Domain.ValueObjects;

namespace CardManagement.Domain.PlatformServices.Reconciliation;

/// <summary>
/// Represents a single parsed line item from an external settlement file.
/// Tracks matching status against internal PaymentRequest records.
/// </summary>
public class SettlementLineItem
{
    public Guid Id { get; private set; }
    public Guid BatchId { get; private set; }
    public string TransactionReference { get; private set; } = string.Empty;
    public string ProcessorReference { get; private set; } = string.Empty;
    public Money Amount { get; private set; } = null!;
    public string Status { get; private set; } = string.Empty;
    public DateOnly TransactionDate { get; private set; }
    public MatchStatus MatchStatus { get; private set; }
    public Guid? MatchedPaymentRequestId { get; private set; }

    private SettlementLineItem() { }

    /// <summary>
    /// Creates a new settlement line item in Unmatched status.
    /// </summary>
    public static SettlementLineItem Create(
        Guid batchId,
        string transactionReference,
        string processorReference,
        Money amount,
        string status,
        DateOnly transactionDate)
    {
        if (batchId == Guid.Empty)
            throw new ArgumentException("Batch ID is required.", nameof(batchId));

        if (string.IsNullOrWhiteSpace(transactionReference))
            throw new ArgumentException("Transaction reference is required.", nameof(transactionReference));

        if (string.IsNullOrWhiteSpace(processorReference))
            throw new ArgumentException("Processor reference is required.", nameof(processorReference));

        if (amount is null)
            throw new ArgumentNullException(nameof(amount));

        if (string.IsNullOrWhiteSpace(status))
            throw new ArgumentException("Status is required.", nameof(status));

        return new SettlementLineItem
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            TransactionReference = transactionReference,
            ProcessorReference = processorReference,
            Amount = amount,
            Status = status,
            TransactionDate = transactionDate,
            MatchStatus = MatchStatus.Unmatched,
            MatchedPaymentRequestId = null
        };
    }

    /// <summary>
    /// Marks this line item as successfully matched to an internal PaymentRequest.
    /// </summary>
    public void MarkMatched(Guid paymentRequestId)
    {
        if (paymentRequestId == Guid.Empty)
            throw new ArgumentException("Payment request ID is required.", nameof(paymentRequestId));

        if (MatchStatus != MatchStatus.Unmatched)
            throw new InvalidOperationException(
                $"Cannot mark as matched from status '{MatchStatus}'. Item must be in Unmatched status.");

        MatchStatus = MatchStatus.Matched;
        MatchedPaymentRequestId = paymentRequestId;
    }

    /// <summary>
    /// Marks this line item as mismatched — it matched a record but with discrepancies.
    /// </summary>
    public void MarkMismatched(Guid paymentRequestId)
    {
        if (paymentRequestId == Guid.Empty)
            throw new ArgumentException("Payment request ID is required.", nameof(paymentRequestId));

        if (MatchStatus != MatchStatus.Unmatched)
            throw new InvalidOperationException(
                $"Cannot mark as mismatched from status '{MatchStatus}'. Item must be in Unmatched status.");

        MatchStatus = MatchStatus.Mismatched;
        MatchedPaymentRequestId = paymentRequestId;
    }
}
