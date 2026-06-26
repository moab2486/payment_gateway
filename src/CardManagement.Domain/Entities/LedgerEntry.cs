using CardManagement.Domain.Enums;

namespace CardManagement.Domain.Entities;

/// <summary>
/// Immutable ledger entry representing a single debit or credit in the double-entry system.
/// Once created, a LedgerEntry cannot be modified (append-only audit trail).
/// </summary>
public class LedgerEntry
{
    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid AccountId { get; private set; }
    public EntryType EntryType { get; private set; }
    public long Amount { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string OperationIdentifier { get; private set; } = string.Empty;
    public DateTime CreatedAtUtc { get; private set; }

    private LedgerEntry() { }

    public static LedgerEntry Create(
        Guid transactionId,
        Guid accountId,
        EntryType entryType,
        long amount,
        string description,
        string operationIdentifier)
    {
        if (transactionId == Guid.Empty)
            throw new ArgumentException("Transaction ID is required.", nameof(transactionId));

        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));

        if (amount <= 0)
            throw new ArgumentException("Amount must be a positive integer.", nameof(amount));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description is required.", nameof(description));

        if (string.IsNullOrWhiteSpace(operationIdentifier))
            throw new ArgumentException("Operation identifier is required.", nameof(operationIdentifier));

        return new LedgerEntry
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            AccountId = accountId,
            EntryType = entryType,
            Amount = amount,
            Description = description,
            OperationIdentifier = operationIdentifier,
            CreatedAtUtc = TruncateToMilliseconds(DateTime.UtcNow)
        };
    }

    private static DateTime TruncateToMilliseconds(DateTime dateTime)
    {
        return new DateTime(
            dateTime.Ticks - (dateTime.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
