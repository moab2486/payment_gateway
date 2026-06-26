using CardManagement.Domain.ValueObjects;

namespace CardManagement.Domain.PlatformServices.Reconciliation;

/// <summary>
/// Represents a discrepancy identified during reconciliation matching.
/// Classified by exception type and tracked through resolution.
/// </summary>
public class ReconciliationException
{
    public Guid Id { get; private set; }
    public Guid BatchId { get; private set; }
    public ExceptionType Type { get; private set; }
    public Guid? SettlementLineItemId { get; private set; }
    public Guid? PaymentRequestId { get; private set; }
    public Money? ExternalAmount { get; private set; }
    public Money? InternalAmount { get; private set; }
    public string? ExternalStatus { get; private set; }
    public string? InternalStatus { get; private set; }
    public ExceptionResolutionStatus ResolutionStatus { get; private set; }
    public Guid? AdjustmentId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    private ReconciliationException() { }

    /// <summary>
    /// Creates a mismatch exception — settlement line matched an internal record but values differ.
    /// </summary>
    public static ReconciliationException CreateMismatch(
        Guid batchId,
        Guid settlementLineItemId,
        Guid paymentRequestId,
        Money externalAmount,
        Money internalAmount,
        string? externalStatus,
        string? internalStatus)
    {
        if (batchId == Guid.Empty)
            throw new ArgumentException("Batch ID is required.", nameof(batchId));

        if (settlementLineItemId == Guid.Empty)
            throw new ArgumentException("Settlement line item ID is required.", nameof(settlementLineItemId));

        if (paymentRequestId == Guid.Empty)
            throw new ArgumentException("Payment request ID is required.", nameof(paymentRequestId));

        if (externalAmount is null)
            throw new ArgumentNullException(nameof(externalAmount));

        if (internalAmount is null)
            throw new ArgumentNullException(nameof(internalAmount));

        return new ReconciliationException
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            Type = ExceptionType.Mismatch,
            SettlementLineItemId = settlementLineItemId,
            PaymentRequestId = paymentRequestId,
            ExternalAmount = externalAmount,
            InternalAmount = internalAmount,
            ExternalStatus = externalStatus,
            InternalStatus = internalStatus,
            ResolutionStatus = ExceptionResolutionStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates an unmatched-external exception — settlement line has no matching internal record.
    /// </summary>
    public static ReconciliationException CreateUnmatchedExternal(
        Guid batchId,
        Guid settlementLineItemId,
        Money externalAmount,
        string? externalStatus)
    {
        if (batchId == Guid.Empty)
            throw new ArgumentException("Batch ID is required.", nameof(batchId));

        if (settlementLineItemId == Guid.Empty)
            throw new ArgumentException("Settlement line item ID is required.", nameof(settlementLineItemId));

        if (externalAmount is null)
            throw new ArgumentNullException(nameof(externalAmount));

        return new ReconciliationException
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            Type = ExceptionType.UnmatchedExternal,
            SettlementLineItemId = settlementLineItemId,
            PaymentRequestId = null,
            ExternalAmount = externalAmount,
            InternalAmount = null,
            ExternalStatus = externalStatus,
            InternalStatus = null,
            ResolutionStatus = ExceptionResolutionStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates an unmatched-internal exception — internal record has no matching settlement line.
    /// </summary>
    public static ReconciliationException CreateUnmatchedInternal(
        Guid batchId,
        Guid paymentRequestId,
        Money internalAmount,
        string? internalStatus)
    {
        if (batchId == Guid.Empty)
            throw new ArgumentException("Batch ID is required.", nameof(batchId));

        if (paymentRequestId == Guid.Empty)
            throw new ArgumentException("Payment request ID is required.", nameof(paymentRequestId));

        if (internalAmount is null)
            throw new ArgumentNullException(nameof(internalAmount));

        return new ReconciliationException
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            Type = ExceptionType.UnmatchedInternal,
            SettlementLineItemId = null,
            PaymentRequestId = paymentRequestId,
            ExternalAmount = null,
            InternalAmount = internalAmount,
            ExternalStatus = null,
            InternalStatus = internalStatus,
            ResolutionStatus = ExceptionResolutionStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Resolves this exception automatically via an adjustment rule.
    /// </summary>
    public void ResolveAutomatically(Guid adjustmentId)
    {
        if (adjustmentId == Guid.Empty)
            throw new ArgumentException("Adjustment ID is required.", nameof(adjustmentId));

        EnsurePending();

        ResolutionStatus = ExceptionResolutionStatus.AutoResolved;
        AdjustmentId = adjustmentId;
    }

    /// <summary>
    /// Resolves this exception manually via an operator-submitted adjustment.
    /// </summary>
    public void ResolveManually(Guid adjustmentId)
    {
        if (adjustmentId == Guid.Empty)
            throw new ArgumentException("Adjustment ID is required.", nameof(adjustmentId));

        EnsurePending();

        ResolutionStatus = ExceptionResolutionStatus.ManualResolved;
        AdjustmentId = adjustmentId;
    }

    /// <summary>
    /// Marks the exception as expired when it was not resolved within the allowed period.
    /// </summary>
    public void MarkExpired()
    {
        EnsurePending();
        ResolutionStatus = ExceptionResolutionStatus.Expired;
    }

    private void EnsurePending()
    {
        if (ResolutionStatus != ExceptionResolutionStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot resolve exception in status '{ResolutionStatus}'. Exception must be in Pending status.");
    }
}
