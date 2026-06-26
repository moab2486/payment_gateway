using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Ledger;
using FsCheck;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Property-based tests for Reversal Net-Zero (Property 14).
/// 
/// **Validates: Requirements 10.2**
/// 
/// For any successfully posted financial transaction, reversing it SHALL create offsetting
/// debit and credit entries such that the net balance change across all affected accounts
/// is zero compared to the state before the original transaction.
/// </summary>
[Trait("Feature", "card-management-system")]
[Trait("Property", "14")]
public class ReversalNetZeroPropertyTests
{
    /// <summary>
    /// **Validates: Requirements 10.2**
    /// 
    /// Property 14: Reversal Net-Zero.
    /// Generate random transaction amounts (1-10000), post a balanced transaction
    /// (asset debit, liability credit), then reverse it via ReverseTransactionAsync.
    /// Assert: net balance change across both accounts is zero after reversal
    /// (i.e., balances return to original values).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property PostThenReverse_ResultsInNetZeroBalanceChange()
    {
        var gen = Gen.Choose(1, 10000);

        return Prop.ForAll(gen.ToArbitrary(), amount =>
        {
            // Arrange: create accounts with sufficient balance
            var initialAssetBalance = (long)amount + 100_000L;
            var initialLiabilityBalance = 0L;

            var assetAccount = Account.Create("ASSET-001", AccountType.Asset, "NGN", initialAssetBalance);
            var liabilityAccount = Account.Create("LIABILITY-001", AccountType.Liability, "NGN", initialLiabilityBalance);

            var accountRepo = new FakeAccountRepository();
            accountRepo.AddAccount(assetAccount);
            accountRepo.AddAccount(liabilityAccount);

            var ledgerRepo = new FakeLedgerEntryRepository();
            var transactionRepo = new FakeTransactionRepository();
            var unitOfWork = new FakeUnitOfWork();
            var logger = new NullLoggerFactory().CreateLogger<LedgerService>();

            var service = new LedgerService(accountRepo, ledgerRepo, transactionRepo, unitOfWork, logger);

            var stan = $"TXN-{Guid.NewGuid():N}";

            // Act Step 1: Post the original transaction
            var transaction = new LedgerTransaction
            {
                Entries = new List<LedgerTransactionEntry>
                {
                    new() { AccountId = assetAccount.Id, EntryType = EntryType.Debit, Amount = amount },
                    new() { AccountId = liabilityAccount.Id, EntryType = EntryType.Credit, Amount = amount }
                },
                OperationIdentifier = stan,
                Description = "Original transaction"
            };

            var postResult = service.PostTransactionAsync(transaction, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (!postResult.IsSuccess)
            {
                return false.Label($"Original transaction failed: {postResult.ErrorMessage}");
            }

            // Set up the fake transaction repo so reversal can look up the original by STAN.
            // The PostTransactionAsync generated the transactionId internally — retrieve it
            // from the result to register for reversal lookup.
            var postedTransactionId = postResult.Value!.Id;
            transactionRepo.RegisterTransaction(stan, postedTransactionId);

            // Act Step 2: Reverse the transaction
            var reverseResult = service.ReverseTransactionAsync(stan, CancellationToken.None)
                .GetAwaiter().GetResult();

            if (!reverseResult.IsSuccess)
            {
                return false.Label($"Reversal failed: {reverseResult.ErrorMessage}");
            }

            // Assert: Balances should return to original values
            var finalAssetBalance = assetAccount.Balance;
            var finalLiabilityBalance = liabilityAccount.Balance;

            var assetBalanceRestored = (finalAssetBalance == initialAssetBalance)
                .Label($"Asset balance should be {initialAssetBalance} but was {finalAssetBalance} " +
                       $"(amount={amount})");

            var liabilityBalanceRestored = (finalLiabilityBalance == initialLiabilityBalance)
                .Label($"Liability balance should be {initialLiabilityBalance} but was {finalLiabilityBalance} " +
                       $"(amount={amount})");

            return assetBalanceRestored.And(liabilityBalanceRestored);
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

    /// <summary>
    /// A fake transaction repository that supports registering transactions for reversal lookup.
    /// Uses a mapping of STAN → TransactionRecord (with correct Id) so ReverseTransactionAsync
    /// can locate the original transaction.
    /// </summary>
    private class FakeTransactionRepository : ITransactionRepository
    {
        private readonly Dictionary<string, TransactionRecord> _transactionsByStan = new();

        /// <summary>
        /// Registers a transaction for STAN-based lookup. Creates a TransactionRecord with the
        /// given ID (matching the ledger entries' TransactionId) so reversal can find it.
        /// </summary>
        public void RegisterTransaction(string stan, Guid transactionId)
        {
            var record = TransactionRecord.Create(
                stan, "0100", Guid.NewGuid(), 0, "NGN", ProcessorType.Interswitch);
            // We need to set the Id to match the ledger entries' transactionId.
            // Use reflection since Id is private set.
            var idProperty = typeof(TransactionRecord).GetProperty(nameof(TransactionRecord.Id));
            idProperty!.SetValue(record, transactionId);
            _transactionsByStan[stan] = record;
        }

        public Task AddAsync(TransactionRecord transaction, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<TransactionRecord?> GetByStanAsync(string systemTraceAuditNumber, CancellationToken cancellationToken = default)
        {
            _transactionsByStan.TryGetValue(systemTraceAuditNumber, out var record);
            return Task.FromResult(record);
        }

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
