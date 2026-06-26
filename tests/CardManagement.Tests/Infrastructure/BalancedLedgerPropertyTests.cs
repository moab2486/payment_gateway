using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Infrastructure.Ledger;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for Balanced Ledger Invariant (Property 11).
/// 
/// **Validates: Requirements 6.1, 6.2**
/// 
/// For any sequence of financial transactions posted to the Ledger Service, the sum of all
/// debit entry amounts across all accounts SHALL equal the sum of all credit entry amounts
/// across all accounts at all times.
/// </summary>
[Trait("Feature", "card-management-system")]
[Trait("Property", "11")]
public class BalancedLedgerPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 6.1, 6.2**
    /// 
    /// Property 11: Balanced Ledger Invariant.
    /// Generate random sequences of 1-50 balanced transactions with random amounts (1-10000).
    /// Post them all to the ledger service.
    /// After all transactions, assert: total debits across all entries == total credits across all entries.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SumOfDebits_Equals_SumOfCredits_AfterAllTransactions()
    {
        var gen = from transactionCount in Gen.Choose(1, 50)
                  from amounts in Gen.ArrayOf(transactionCount, Gen.Choose(1, 10000))
                  select amounts;

        return Prop.ForAll(gen.ToArbitrary(), amounts =>
        {
            // Arrange: create an asset account with large balance to avoid rejections
            // and a liability account as the credit target.
            var assetAccount = Account.Create("ASSET-001", AccountType.Asset, "NGN",
                (long)amounts.Sum(a => (long)a) + 1_000_000L);
            var liabilityAccount = Account.Create("LIABILITY-001", AccountType.Liability, "NGN", 0);

            var accountRepo = new FakeAccountRepository();
            accountRepo.AddAccount(assetAccount);
            accountRepo.AddAccount(liabilityAccount);

            var ledgerRepo = new FakeLedgerEntryRepository();
            var transactionRepo = new FakeTransactionRepository();
            var unitOfWork = new FakeUnitOfWork();
            var logger = new NullLoggerFactory().CreateLogger<LedgerService>();

            var service = new LedgerService(accountRepo, ledgerRepo, transactionRepo, unitOfWork, logger);

            // Act: post each transaction sequentially
            for (int i = 0; i < amounts.Length; i++)
            {
                var transaction = new LedgerTransaction
                {
                    Entries = new List<LedgerTransactionEntry>
                    {
                        new() { AccountId = assetAccount.Id, EntryType = EntryType.Debit, Amount = amounts[i] },
                        new() { AccountId = liabilityAccount.Id, EntryType = EntryType.Credit, Amount = amounts[i] }
                    },
                    OperationIdentifier = $"TXN-{i:D4}",
                    Description = $"Transaction {i}"
                };

                var result = service.PostTransactionAsync(transaction, CancellationToken.None)
                    .GetAwaiter().GetResult();

                if (!result.IsSuccess)
                {
                    return false.Label(
                        $"Transaction {i} failed unexpectedly: {result.ErrorMessage}");
                }
            }

            // Assert: sum of all debits == sum of all credits
            var totalDebits = ledgerRepo.AddedEntries
                .Where(e => e.EntryType == EntryType.Debit)
                .Sum(e => e.Amount);

            var totalCredits = ledgerRepo.AddedEntries
                .Where(e => e.EntryType == EntryType.Credit)
                .Sum(e => e.Amount);

            return (totalDebits == totalCredits)
                .Label($"Total debits ({totalDebits}) must equal total credits ({totalCredits}) " +
                       $"after {amounts.Length} transactions");
        });
    }

    #region Fake Implementations

    private class FakeAccountRepository : IAccountRepository
    {
        private readonly Dictionary<Guid, Account> _accounts = new();

        public void AddAccount(Account account)
        {
            _accounts[account.Id] = account;
        }

        public Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _accounts.TryGetValue(id, out var account);
            return Task.FromResult(account);
        }

        public Task<Account?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default)
        {
            _accounts.TryGetValue(id, out var account);
            return Task.FromResult(account);
        }

        public Task UpdateBalanceAsync(Account account, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private class FakeLedgerEntryRepository : ILedgerEntryRepository
    {
        public List<LedgerEntry> AddedEntries { get; } = new();

        public Task AddRangeAsync(IEnumerable<LedgerEntry> entries, CancellationToken cancellationToken = default)
        {
            AddedEntries.AddRange(entries);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LedgerEntry>> GetByTransactionIdAsync(Guid transactionId, CancellationToken cancellationToken = default)
        {
            var entries = AddedEntries.Where(e => e.TransactionId == transactionId).ToList();
            return Task.FromResult<IReadOnlyList<LedgerEntry>>(entries);
        }
    }

    private class FakeTransactionRepository : ITransactionRepository
    {
        public Task AddAsync(TransactionRecord transaction, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<TransactionRecord?> GetByStanAsync(string systemTraceAuditNumber, CancellationToken cancellationToken = default)
            => Task.FromResult<TransactionRecord?>(null);

        public Task UpdateAsync(TransactionRecord transaction, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public Task BeginTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ExecuteSqlAsync(string sql, CancellationToken ct = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    #endregion
}
