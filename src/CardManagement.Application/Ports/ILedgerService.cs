using CardManagement.Application.DTOs;

namespace CardManagement.Application.Ports;

/// <summary>
/// Port interface for the double-entry ledger system. Records all financial transactions
/// as balanced debit/credit entries with strict concurrency control using PostgreSQL
/// row-level locks.
/// </summary>
public interface ILedgerService
{
    /// <summary>
    /// Posts a financial transaction to the ledger as balanced debit/credit entries.
    /// Acquires row-level locks on affected accounts, validates sufficient funds for
    /// debit-type accounts, and updates balances atomically within a single DB transaction.
    /// </summary>
    /// <param name="transaction">The ledger transaction with balanced entries.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A result containing the transaction record or an error (insufficient funds, lock timeout, etc.).</returns>
    Task<Result<TransactionRecordDto>> PostTransactionAsync(LedgerTransaction transaction, CancellationToken ct);

    /// <summary>
    /// Reverses a previously posted transaction by creating offsetting debit and credit entries.
    /// Locates the original transaction by its System Trace Audit Number.
    /// </summary>
    /// <param name="systemTraceAuditNumber">The STAN of the original transaction to reverse.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A result containing the reversal transaction record or an error (original not found, etc.).</returns>
    Task<Result<TransactionRecordDto>> ReverseTransactionAsync(string systemTraceAuditNumber, CancellationToken ct);

    /// <summary>
    /// Retrieves the current balance for a specified account.
    /// </summary>
    /// <param name="accountId">The unique identifier of the account.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A result containing the account balance or an error (account not found, etc.).</returns>
    Task<Result<AccountBalance>> GetBalanceAsync(Guid accountId, CancellationToken ct);
}
