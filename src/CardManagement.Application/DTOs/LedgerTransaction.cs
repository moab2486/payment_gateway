namespace CardManagement.Application.DTOs;

/// <summary>
/// DTO representing a ledger transaction consisting of balanced debit/credit entries.
/// </summary>
public record LedgerTransaction
{
    /// <summary>
    /// The entries that make up this transaction (at least one debit and one credit).
    /// </summary>
    public IReadOnlyList<LedgerTransactionEntry> Entries { get; init; } = Array.Empty<LedgerTransactionEntry>();

    /// <summary>
    /// A reference identifier for the originating operation.
    /// </summary>
    public string OperationIdentifier { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable description of the transaction.
    /// </summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// A single entry within a ledger transaction.
/// </summary>
public record LedgerTransactionEntry
{
    /// <summary>
    /// The account to debit or credit.
    /// </summary>
    public Guid AccountId { get; init; }

    /// <summary>
    /// The type of entry (Debit or Credit).
    /// </summary>
    public Domain.Enums.EntryType EntryType { get; init; }

    /// <summary>
    /// The amount in the smallest currency unit (positive integer).
    /// </summary>
    public long Amount { get; init; }
}
