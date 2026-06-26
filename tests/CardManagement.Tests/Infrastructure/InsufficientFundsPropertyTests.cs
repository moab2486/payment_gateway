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
/// Property-based tests for Insufficient Funds Rejection (Property 12).
/// 
/// **Validates: Requirements 6.5**
/// 
/// For any transaction where the debit amount exceeds the current balance of a debit-type
/// account (asset or expense), the Ledger Service SHALL reject the transaction with an
/// insufficient-funds error AND the ledger SHALL contain zero new entries from that
/// rejected transaction.
/// </summary>
[Trait("Feature", "card-management-system")]
[Trait("Property", "12")]
public class InsufficientFundsPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 6.5**
    /// 
    /// Property 12: Insufficient Funds Rejection.
    /// Generate random asset account balances (1-10000) and debit amounts that exceed the balance.
    /// Assert: transaction rejected with error code "51" (insufficient funds) AND zero new
    /// ledger entries created.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property AssetAccount_DebitExceedingBalance_IsRejectedWithNoEntries()
    {
        var gen = from balance in Gen.Choose(1, 10000)
                  from excess in Gen.Choose(1, 10000)
                  let debitAmount = (long)balance + (long)excess
                  select new { Balance = (long)balance, DebitAmount = debitAmount };

        return Prop.ForAll(gen.ToArbitrary(), testCase =>
        {
            // Arrange
            var assetAccount = Account.Create("ASSET-001", AccountType.Asset, "NGN", testCase.Balance);
            var liabilityAccount = Account.Create("LIABILITY-001", AccountType.Liability, "NGN", 0);

            var accountRepo = new FakeAccountRepository();
            accountRepo.AddAccount(assetAccount);
            accountRepo.AddAccount(liabilityAccount);

            var ledgerRepo = new FakeLedgerEntryRepository();
            var service = CreateService(accountRepo: accountRepo, ledgerRepo: ledgerRepo);

            var transaction = new LedgerTransaction
            {
                Entries = new List<LedgerTransactionEntry>
                {
                    new() { AccountId = assetAccount.Id, EntryType = EntryType.Debit, Amount = testCase.DebitAmount },
                    new() { AccountId = liabilityAccount.Id, EntryType = EntryType.Credit, Amount = testCase.DebitAmount }
                },
                OperationIdentifier = $"TXN-{Guid.NewGuid():N}",
                Description = "Insufficient funds test transaction"
            };

            // Act
            var result = service.PostTransactionAsync(transaction, CancellationToken.None)
                .GetAwaiter().GetResult();

            // Assert 1: Transaction must be rejected
            var rejected = (!result.IsSuccess)
                .Label($"Transaction should be rejected when debit {testCase.DebitAmount} > balance {testCase.Balance}");

            // Assert 2: Error code must be "51" (insufficient funds)
            var errorCode = (result.ErrorCode == "51")
                .Label($"Error code should be '51' but was '{result.ErrorCode}' " +
                       $"(balance={testCase.Balance}, debit={testCase.DebitAmount})");

            // Assert 3: Zero new ledger entries should be created
            var noEntries = (ledgerRepo.AddedEntries.Count == 0)
                .Label($"Expected 0 ledger entries but found {ledgerRepo.AddedEntries.Count} " +
                       $"(balance={testCase.Balance}, debit={testCase.DebitAmount})");

            return rejected.And(errorCode).And(noEntries);
        });
    }

    /// <summary>
    /// **Validates: Requirements 6.5**
    /// 
    /// Property 12: Insufficient Funds Rejection for Expense accounts.
    /// Generate random expense account balances (1-10000) and debit amounts that exceed the balance.
    /// Assert: transaction rejected with error code "51" AND zero new entries created.
    /// Expense accounts are also debit-normal and cannot go negative.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ExpenseAccount_DebitExceedingBalance_IsRejectedWithNoEntries()
    {
        var gen = from balance in Gen.Choose(1, 10000)
                  from excess in Gen.Choose(1, 10000)
                  let debitAmount = (long)balance + (long)excess
                  select new { Balance = (long)balance, DebitAmount = debitAmount };

        return Prop.ForAll(gen.ToArbitrary(), testCase =>
        {
            // Arrange
            var expenseAccount = Account.Create("EXPENSE-001", AccountType.Expense, "NGN", testCase.Balance);
            var revenueAccount = Account.Create("REVENUE-001", AccountType.Revenue, "NGN", 0);

            var accountRepo = new FakeAccountRepository();
            accountRepo.AddAccount(expenseAccount);
            accountRepo.AddAccount(revenueAccount);

            var ledgerRepo = new FakeLedgerEntryRepository();
            var service = CreateService(accountRepo: accountRepo, ledgerRepo: ledgerRepo);

            var transaction = new LedgerTransaction
            {
                Entries = new List<LedgerTransactionEntry>
                {
                    new() { AccountId = expenseAccount.Id, EntryType = EntryType.Debit, Amount = testCase.DebitAmount },
                    new() { AccountId = revenueAccount.Id, EntryType = EntryType.Credit, Amount = testCase.DebitAmount }
                },
                OperationIdentifier = $"TXN-{Guid.NewGuid():N}",
                Description = "Expense insufficient funds test"
            };

            // Act
            var result = service.PostTransactionAsync(transaction, CancellationToken.None)
                .GetAwaiter().GetResult();

            // Assert 1: Transaction must be rejected
            var rejected = (!result.IsSuccess)
                .Label($"Transaction should be rejected when debit {testCase.DebitAmount} > expense balance {testCase.Balance}");

            // Assert 2: Error code must be "51"
            var errorCode = (result.ErrorCode == "51")
                .Label($"Error code should be '51' but was '{result.ErrorCode}' " +
                       $"(balance={testCase.Balance}, debit={testCase.DebitAmount})");

            // Assert 3: Zero new ledger entries
            var noEntries = (ledgerRepo.AddedEntries.Count == 0)
                .Label($"Expected 0 ledger entries but found {ledgerRepo.AddedEntries.Count}");

            return rejected.And(errorCode).And(noEntries);
        });
    }

    #region Helper Methods and Fakes

    private static LedgerService CreateService(
        FakeAccountRepository? accountRepo = null,
        FakeLedgerEntryRepository? ledgerRepo = null,
        FakeTransactionRepository? transactionRepo = null,
        FakeUnitOfWork? unitOfWork = null)
    {
        var accounts = accountRepo ?? new FakeAccountRepository();
        var entries = ledgerRepo ?? new FakeLedgerEntryRepository();
        var transactions = transactionRepo ?? new FakeTransactionRepository();
        var uow = unitOfWork ?? new FakeUnitOfWork();
        var logger = new NullLoggerFactory().CreateLogger<LedgerService>();
        return new LedgerService(accounts, entries, transactions, uow, logger);
    }

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
            return Task.FromResult<IReadOnlyList<LedgerEntry>>(Array.Empty<LedgerEntry>());
        }
    }

    private class FakeTransactionRepository : ITransactionRepository
    {
        public Task AddAsync(TransactionRecord transaction, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<TransactionRecord?> GetByStanAsync(string systemTraceAuditNumber, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<TransactionRecord?>(null);
        }

        public Task UpdateAsync(TransactionRecord transaction, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
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
