using CardManagement.Domain.Entities;

namespace CardManagement.Application.Ports.Repositories;

/// <summary>
/// Repository interface for TransactionRecord persistence operations.
/// </summary>
public interface ITransactionRepository
{
    /// <summary>
    /// Persists a new transaction record.
    /// </summary>
    Task AddAsync(TransactionRecord transaction, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a transaction record by its System Trace Audit Number (STAN).
    /// Used for reversal lookups and duplicate detection.
    /// </summary>
    Task<TransactionRecord?> GetByStanAsync(string systemTraceAuditNumber, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists updates to an existing transaction record (e.g., status, response code, pipeline step).
    /// </summary>
    Task UpdateAsync(TransactionRecord transaction, CancellationToken cancellationToken = default);
}
