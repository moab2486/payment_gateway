using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;

namespace CardManagement.Domain.Entities;

/// <summary>
/// Represents a dispute raised against a payment transaction.
/// Tracks the full dispute lifecycle from opening to resolution.
/// </summary>
public class DisputeRecord
{
    public Guid Id { get; private set; }
    public string TransactionReference { get; private set; } = string.Empty;
    public string ReasonCode { get; private set; } = string.Empty;
    public Money Amount { get; private set; } = null!;
    public string? Evidence { get; private set; }
    public DisputeStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }

    private DisputeRecord() { }

    public static DisputeRecord Create(
        string transactionReference,
        string reasonCode,
        Money amount,
        string? evidence)
    {
        if (string.IsNullOrWhiteSpace(transactionReference))
            throw new ArgumentException("Transaction reference is required.", nameof(transactionReference));

        if (string.IsNullOrWhiteSpace(reasonCode))
            throw new ArgumentException("Reason code is required.", nameof(reasonCode));

        if (amount is null)
            throw new ArgumentNullException(nameof(amount));

        var now = TruncateToMilliseconds(DateTime.UtcNow);

        return new DisputeRecord
        {
            Id = Guid.NewGuid(),
            TransactionReference = transactionReference,
            ReasonCode = reasonCode,
            Amount = amount,
            Evidence = evidence,
            Status = DisputeStatus.Opened,
            CreatedAtUtc = now
        };
    }

    public void MarkUnderReview()
    {
        if (Status != DisputeStatus.Opened)
            throw new InvalidOperationException("Can only review an opened dispute.");

        Status = DisputeStatus.UnderReview;
    }

    public void Escalate()
    {
        if (Status != DisputeStatus.UnderReview)
            throw new InvalidOperationException("Can only escalate a dispute under review.");

        Status = DisputeStatus.Escalated;
    }

    public void ResolveInFavour()
    {
        if (Status is not (DisputeStatus.UnderReview or DisputeStatus.Escalated))
            throw new InvalidOperationException("Can only resolve a dispute that is under review or escalated.");

        Status = DisputeStatus.ResolvedInFavour;
        ResolvedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    public void ResolveAgainst()
    {
        if (Status is not (DisputeStatus.UnderReview or DisputeStatus.Escalated))
            throw new InvalidOperationException("Can only resolve a dispute that is under review or escalated.");

        Status = DisputeStatus.ResolvedAgainst;
        ResolvedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    public void Close()
    {
        if (Status is not (DisputeStatus.ResolvedInFavour or DisputeStatus.ResolvedAgainst))
            throw new InvalidOperationException("Can only close a resolved dispute.");

        Status = DisputeStatus.Closed;
    }

    private static DateTime TruncateToMilliseconds(DateTime dateTime)
    {
        return new DateTime(
            dateTime.Ticks - (dateTime.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
