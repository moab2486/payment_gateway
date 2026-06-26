using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.Ledger;

/// <summary>
/// Records financial transactions as balanced double-entry journal entries with strict
/// concurrency control. Uses PostgreSQL row-level locks (SELECT FOR UPDATE) with a
/// configurable lock timeout to prevent deadlocks and ensure atomic balance updates.
/// All ledger entries are immutable after creation (append-only audit trail).
/// </summary>
public class LedgerService : ILedgerService
{
    private readonly IAccountRepository _accountRepository;
    private readonly ILedgerEntryRepository _ledgerEntryRepository;
    private readonly ITransactionRepository _transactionRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LedgerService> _logger;

    private const string LockTimeoutSql = "SET LOCAL lock_timeout = '5s'";
    private const string InsufficientFundsErrorCode = "51";
    private const string LockTimeoutErrorCode = "96";
    private const string ValidationErrorCode = "30";

    public LedgerService(
        IAccountRepository accountRepository,
        ILedgerEntryRepository ledgerEntryRepository,
        ITransactionRepository transactionRepository,
        IUnitOfWork unitOfWork,
        ILogger<LedgerService> logger)
    {
        _accountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
        _ledgerEntryRepository = ledgerEntryRepository ?? throw new ArgumentNullException(nameof(ledgerEntryRepository));
        _transactionRepository = transactionRepository ?? throw new ArgumentNullException(nameof(transactionRepository));
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<TransactionRecordDto>> PostTransactionAsync(
        LedgerTransaction transaction, CancellationToken ct)
    {
        // Validate the transaction structure
        var validationResult = ValidateTransaction(transaction);
        if (!validationResult.IsSuccess)
        {
            return Result<TransactionRecordDto>.Failure(
                validationResult.ErrorMessage!, validationResult.ErrorCode);
        }

        try
        {
            await _unitOfWork.BeginTransactionAsync(ct).ConfigureAwait(false);

            // Set PostgreSQL lock timeout to 5 seconds
            await _unitOfWork.ExecuteSqlAsync(LockTimeoutSql, ct).ConfigureAwait(false);

            var transactionId = Guid.NewGuid();

            // Acquire row-level locks on all affected accounts (SELECT FOR UPDATE)
            // Sort by account ID to prevent deadlocks from lock ordering
            var distinctAccountIds = transaction.Entries
                .Select(e => e.AccountId)
                .Distinct()
                .OrderBy(id => id)
                .ToList();

            var accounts = new Dictionary<Guid, Account>();
            foreach (var accountId in distinctAccountIds)
            {
                var account = await _accountRepository.GetByIdForUpdateAsync(accountId, ct)
                    .ConfigureAwait(false);

                if (account is null)
                {
                    await _unitOfWork.RollbackAsync(ct).ConfigureAwait(false);
                    return Result<TransactionRecordDto>.Failure(
                        $"Account {accountId} not found.", ValidationErrorCode);
                }

                accounts[accountId] = account;
            }

            // Validate sufficient balance for debit-type accounts (Asset/Expense)
            var insufficientFundsResult = ValidateSufficientFunds(transaction.Entries, accounts);
            if (!insufficientFundsResult.IsSuccess)
            {
                await _unitOfWork.RollbackAsync(ct).ConfigureAwait(false);
                return Result<TransactionRecordDto>.Failure(
                    insufficientFundsResult.ErrorMessage!, insufficientFundsResult.ErrorCode);
            }

            // Apply balance updates to accounts
            foreach (var entry in transaction.Entries)
            {
                var account = accounts[entry.AccountId];
                if (entry.EntryType == EntryType.Debit)
                {
                    account.Debit(entry.Amount);
                }
                else
                {
                    account.Credit(entry.Amount);
                }

                await _accountRepository.UpdateBalanceAsync(account, ct).ConfigureAwait(false);
            }

            // Create immutable ledger entries (append-only audit trail)
            var ledgerEntries = transaction.Entries.Select(entry =>
                LedgerEntry.Create(
                    transactionId,
                    entry.AccountId,
                    entry.EntryType,
                    entry.Amount,
                    transaction.Description,
                    transaction.OperationIdentifier))
                .ToList();

            await _ledgerEntryRepository.AddRangeAsync(ledgerEntries, ct).ConfigureAwait(false);

            // Commit the entire transaction atomically
            await _unitOfWork.CommitAsync(ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Transaction {TransactionId} posted successfully with {EntryCount} entries for operation {OperationIdentifier}",
                transactionId, ledgerEntries.Count, transaction.OperationIdentifier);

            var dto = new TransactionRecordDto
            {
                Id = transactionId,
                SystemTraceAuditNumber = transaction.OperationIdentifier,
                MessageType = "0100",
                ResponseCode = "00",
                Amount = transaction.Entries.Where(e => e.EntryType == EntryType.Debit).Sum(e => e.Amount),
                Currency = string.Empty,
                ProcessorType = ProcessorType.Interswitch,
                Status = "Approved"
            };

            return Result<TransactionRecordDto>.Success(dto);
        }
        catch (Exception ex) when (IsLockTimeoutException(ex))
        {
            _logger.LogWarning(ex,
                "Lock timeout acquiring row locks for transaction with operation {OperationIdentifier}",
                transaction.OperationIdentifier);

            try
            {
                await _unitOfWork.RollbackAsync(ct).ConfigureAwait(false);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Failed to rollback after lock timeout");
            }

            return Result<TransactionRecordDto>.Failure(
                "Lock timeout: unable to acquire row-level locks within 5 seconds.",
                LockTimeoutErrorCode);
        }
        catch (OperationCanceledException)
        {
            try
            {
                await _unitOfWork.RollbackAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // Best effort rollback on cancellation
            }

            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error posting transaction with operation {OperationIdentifier}",
                transaction.OperationIdentifier);

            try
            {
                await _unitOfWork.RollbackAsync(ct).ConfigureAwait(false);
            }
            catch (Exception rollbackEx)
            {
                _logger.LogError(rollbackEx, "Failed to rollback after unexpected error");
            }

            return Result<TransactionRecordDto>.Failure(
                "An unexpected error occurred while posting the transaction.",
                LockTimeoutErrorCode);
        }
    }

    /// <inheritdoc />
    public async Task<Result<TransactionRecordDto>> ReverseTransactionAsync(
        string systemTraceAuditNumber, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(systemTraceAuditNumber))
        {
            return Result<TransactionRecordDto>.Failure(
                "System trace audit number is required.", ValidationErrorCode);
        }

        var originalTransaction = await _transactionRepository.GetByStanAsync(systemTraceAuditNumber, ct)
            .ConfigureAwait(false);

        if (originalTransaction is null)
        {
            return Result<TransactionRecordDto>.Failure(
                $"Original transaction with STAN '{systemTraceAuditNumber}' not found.",
                ValidationErrorCode);
        }

        // Get original ledger entries to create offsetting entries
        var originalEntries = await _ledgerEntryRepository.GetByTransactionIdAsync(originalTransaction.Id, ct)
            .ConfigureAwait(false);

        if (originalEntries.Count == 0)
        {
            return Result<TransactionRecordDto>.Failure(
                $"No ledger entries found for transaction with STAN '{systemTraceAuditNumber}'.",
                ValidationErrorCode);
        }

        // Create offsetting entries (swap debit/credit)
        var reversalEntries = originalEntries.Select(e => new LedgerTransactionEntry
        {
            AccountId = e.AccountId,
            EntryType = e.EntryType == EntryType.Debit ? EntryType.Credit : EntryType.Debit,
            Amount = e.Amount
        }).ToList();

        var reversalTransaction = new LedgerTransaction
        {
            Entries = reversalEntries,
            OperationIdentifier = $"REV-{systemTraceAuditNumber}",
            Description = $"Reversal of transaction {systemTraceAuditNumber}"
        };

        return await PostTransactionAsync(reversalTransaction, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<AccountBalance>> GetBalanceAsync(Guid accountId, CancellationToken ct)
    {
        var account = await _accountRepository.GetByIdAsync(accountId, ct).ConfigureAwait(false);

        if (account is null)
        {
            return Result<AccountBalance>.Failure(
                $"Account {accountId} not found.", ValidationErrorCode);
        }

        var balance = new AccountBalance
        {
            AccountId = account.Id,
            Balance = account.Balance,
            Currency = account.Currency
        };

        return Result<AccountBalance>.Success(balance);
    }

    /// <summary>
    /// Validates the transaction structure: must have entries, at least one debit and one credit,
    /// all amounts must be positive, and total debits must equal total credits.
    /// </summary>
    private static Result ValidateTransaction(LedgerTransaction transaction)
    {
        if (transaction is null)
        {
            return Result.Failure("Transaction cannot be null.", ValidationErrorCode);
        }

        if (transaction.Entries is null || transaction.Entries.Count == 0)
        {
            return Result.Failure("Transaction must contain at least one entry.", ValidationErrorCode);
        }

        var hasDebit = transaction.Entries.Any(e => e.EntryType == EntryType.Debit);
        var hasCredit = transaction.Entries.Any(e => e.EntryType == EntryType.Credit);

        if (!hasDebit || !hasCredit)
        {
            return Result.Failure(
                "Transaction must contain at least one debit and one credit entry.",
                ValidationErrorCode);
        }

        if (transaction.Entries.Any(e => e.Amount <= 0))
        {
            return Result.Failure(
                "All entry amounts must be positive integers in the smallest currency unit.",
                ValidationErrorCode);
        }

        if (transaction.Entries.Any(e => e.AccountId == Guid.Empty))
        {
            return Result.Failure("All entries must reference a valid account.", ValidationErrorCode);
        }

        var totalDebits = transaction.Entries
            .Where(e => e.EntryType == EntryType.Debit)
            .Sum(e => e.Amount);

        var totalCredits = transaction.Entries
            .Where(e => e.EntryType == EntryType.Credit)
            .Sum(e => e.Amount);

        if (totalDebits != totalCredits)
        {
            return Result.Failure(
                $"Transaction is not balanced: total debits ({totalDebits}) != total credits ({totalCredits}).",
                ValidationErrorCode);
        }

        if (string.IsNullOrWhiteSpace(transaction.OperationIdentifier))
        {
            return Result.Failure("Operation identifier is required.", ValidationErrorCode);
        }

        if (string.IsNullOrWhiteSpace(transaction.Description))
        {
            return Result.Failure("Description is required.", ValidationErrorCode);
        }

        return Result.Success();
    }

    /// <summary>
    /// Validates that debit-type accounts (Asset/Expense) have sufficient balance
    /// for the requested debit amount. Credit-type accounts (Liability/Revenue) can
    /// accept debits that result in negative balances.
    /// </summary>
    private static Result ValidateSufficientFunds(
        IReadOnlyList<LedgerTransactionEntry> entries,
        Dictionary<Guid, Account> accounts)
    {
        // Calculate net debit per account (debit increases decrease balance)
        var netDebitsPerAccount = entries
            .GroupBy(e => e.AccountId)
            .ToDictionary(
                g => g.Key,
                g => g.Where(e => e.EntryType == EntryType.Debit).Sum(e => e.Amount)
                   - g.Where(e => e.EntryType == EntryType.Credit).Sum(e => e.Amount));

        foreach (var (accountId, netDebit) in netDebitsPerAccount)
        {
            if (netDebit <= 0) continue; // Net credit or zero — no risk of negative balance

            var account = accounts[accountId];

            // Only debit-type accounts (Asset/Expense) cannot go negative
            if (account.IsDebitNormal && account.Balance - netDebit < 0)
            {
                return Result.Failure(
                    $"Insufficient funds: account {accountId} (type: {account.AccountType}) " +
                    $"has balance {account.Balance} but requires net debit of {netDebit}.",
                    InsufficientFundsErrorCode);
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// Determines if an exception is caused by a PostgreSQL lock_timeout.
    /// </summary>
    private static bool IsLockTimeoutException(Exception ex)
    {
        // PostgreSQL error code 55P03 = lock_not_available
        // Npgsql wraps this as a PostgresException with SqlState "55P03"
        var message = ex.Message;
        if (message.Contains("lock_not_available", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("55P03", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("lock timeout", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (ex.InnerException is not null)
        {
            return IsLockTimeoutException(ex.InnerException);
        }

        return false;
    }
}
