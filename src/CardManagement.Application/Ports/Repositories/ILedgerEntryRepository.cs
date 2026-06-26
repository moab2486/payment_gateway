using CardManagement.Domain.Entities;

namespace CardManagement.Application.Ports.Repositories;

/// <summary>
/// Repository interface for LedgerEntry persistence operations.
/// Ledger entries are immutable (append-only) once created.
/// </summary>
public interface ILedgerEntryRepository
{
    /// <summary>
    /// Persists a batch of ledger entries atomically (e.g., matching debit and credit pair).
    /// </summary>
    Task AddRangeAsync(IEnumerable<LedgerEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all ledger entries associated with a given transaction.
    /// </summary>
    Task<IReadOnlyList<LedgerEntry>> GetByTransactionIdAsync(Guid transactionId, CancellationToken cancellationToken = default);
}
