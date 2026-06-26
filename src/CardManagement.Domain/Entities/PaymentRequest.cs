using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;

namespace CardManagement.Domain.Entities;

/// <summary>
/// Represents a payment request submitted for processing through the payment integration layer.
/// Tracks the full lifecycle from creation through completion or failure.
/// </summary>
public class PaymentRequest
{
    public Guid Id { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string TransactionReference { get; private set; } = string.Empty;
    public PaymentTransactionType TransactionType { get; private set; }
    public Money Amount { get; private set; } = null!;
    public string SourceAccount { get; private set; } = string.Empty;
    public string DestinationAccount { get; private set; } = string.Empty;
    public PaymentChannel Channel { get; private set; }
    public PaymentStatus Status { get; private set; }
    public int? RiskScore { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }

    private PaymentRequest() { }

    public static PaymentRequest Create(
        string idempotencyKey,
        string transactionReference,
        PaymentTransactionType transactionType,
        Money amount,
        string sourceAccount,
        string destinationAccount,
        PaymentChannel channel)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("Idempotency key is required.", nameof(idempotencyKey));

        if (string.IsNullOrWhiteSpace(transactionReference))
            throw new ArgumentException("Transaction reference is required.", nameof(transactionReference));

        if (amount is null)
            throw new ArgumentNullException(nameof(amount));

        if (string.IsNullOrWhiteSpace(sourceAccount))
            throw new ArgumentException("Source account is required.", nameof(sourceAccount));

        if (string.IsNullOrWhiteSpace(destinationAccount))
            throw new ArgumentException("Destination account is required.", nameof(destinationAccount));

        var now = TruncateToMilliseconds(DateTime.UtcNow);

        return new PaymentRequest
        {
            Id = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            TransactionReference = transactionReference,
            TransactionType = transactionType,
            Amount = amount,
            SourceAccount = sourceAccount,
            DestinationAccount = destinationAccount,
            Channel = channel,
            Status = PaymentStatus.Created,
            CreatedAtUtc = now
        };
    }

    public void MarkPendingFraudCheck()
    {
        EnsureNotTerminal();
        Status = PaymentStatus.PendingFraudCheck;
    }

    public void Approve(int riskScore)
    {
        if (Status != PaymentStatus.PendingFraudCheck)
            throw new InvalidOperationException("Can only approve from PendingFraudCheck status.");

        RiskScore = riskScore;
        Status = PaymentStatus.Approved;
    }

    public void MarkRouting()
    {
        if (Status != PaymentStatus.Approved)
            throw new InvalidOperationException("Can only route from Approved status.");

        Status = PaymentStatus.Routing;
    }

    public void MarkProcessing()
    {
        if (Status != PaymentStatus.Routing)
            throw new InvalidOperationException("Can only process from Routing status.");

        Status = PaymentStatus.Processing;
    }

    public void Complete()
    {
        if (Status != PaymentStatus.Processing)
            throw new InvalidOperationException("Can only complete from Processing status.");

        Status = PaymentStatus.Completed;
        CompletedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    public void Fail()
    {
        EnsureNotTerminal();
        Status = PaymentStatus.Failed;
        CompletedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    public void Reverse()
    {
        if (Status != PaymentStatus.Completed)
            throw new InvalidOperationException("Can only reverse a completed payment.");

        Status = PaymentStatus.Reversed;
        CompletedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    public void Dispute()
    {
        if (Status != PaymentStatus.Completed)
            throw new InvalidOperationException("Can only dispute a completed payment.");

        Status = PaymentStatus.Disputed;
    }

    public void MarkManualReview(int riskScore)
    {
        if (Status != PaymentStatus.PendingFraudCheck)
            throw new InvalidOperationException("Can only mark for manual review from PendingFraudCheck status.");

        RiskScore = riskScore;
        Status = PaymentStatus.ManualReview;
    }

    private void EnsureNotTerminal()
    {
        if (Status is PaymentStatus.Completed or PaymentStatus.Failed or PaymentStatus.Reversed)
            throw new InvalidOperationException($"Cannot transition from terminal status '{Status}'.");
    }

    private static DateTime TruncateToMilliseconds(DateTime dateTime)
    {
        return new DateTime(
            dateTime.Ticks - (dateTime.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
