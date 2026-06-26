using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Infrastructure.Ledger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Unit tests for LedgerService covering double-entry posting, balance validation,
/// lock timeout handling, reversal logic, and atomic transaction guarantees.
/// </summary>
public class LedgerServiceTests
{
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

    private static Account CreateAssetAccount(long balance = 10000)
    {
        return Account.Create("ACC-001", AccountType.Asset, "NGN", balance);
    }

    private static Account CreateLiabilityAccount(long balance = 0)
    {
        return Account.Create("ACC-002", AccountType.Liability, "NGN", balance);
    }

    private static Account CreateExpenseAccount(long balance = 5000)
    {
        return Account.Create("ACC-003", AccountType.Expense, "NGN", balance);
    }

    private static LedgerTransaction CreateBalancedTransaction(
        Guid debitAccountId, Guid creditAccountId, long amount)
    {
        return new LedgerTransaction
        {
            Entries = new List<LedgerTransactionEntry>
            {
                new() { AccountId = debitAccountId, EntryType = EntryType.Debit, Amount = amount },
                new() { AccountId = creditAccountId, EntryType = EntryType.Credit, Amount = amount }
            },
            OperationIdentifier = "TXN-001",
            Description = "Test transaction"
        };
    }

    [Fact]
    public async Task PostTransactionAsync_BalancedTransaction_ReturnsSuccess()
    {
        var assetAccount = CreateAssetAccount(10000);
        var liabilityAccount = CreateLiabilityAccount();
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(assetAccount);
        accountRepo.AddAccount(liabilityAccount);

        var service = CreateService(accountRepo: accountRepo);
        var transaction = CreateBalancedTransaction(assetAccount.Id, liabilityAccount.Id, 5000);

        var result = await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("00", result.Value!.ResponseCode);
    }

    [Fact]
    public async Task PostTransactionAsync_CreatesDebitAndCreditEntries()
    {
        var assetAccount = CreateAssetAccount(10000);
        var liabilityAccount = CreateLiabilityAccount();
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(assetAccount);
        accountRepo.AddAccount(liabilityAccount);
        var ledgerRepo = new FakeLedgerEntryRepository();

        var service = CreateService(accountRepo: accountRepo, ledgerRepo: ledgerRepo);
        var transaction = CreateBalancedTransaction(assetAccount.Id, liabilityAccount.Id, 5000);

        await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.Equal(2, ledgerRepo.AddedEntries.Count);
        Assert.Contains(ledgerRepo.AddedEntries, e => e.EntryType == EntryType.Debit && e.Amount == 5000);
        Assert.Contains(ledgerRepo.AddedEntries, e => e.EntryType == EntryType.Credit && e.Amount == 5000);
    }

    [Fact]
    public async Task PostTransactionAsync_UpdatesAccountBalances()
    {
        var assetAccount = CreateAssetAccount(10000);
        var liabilityAccount = CreateLiabilityAccount(0);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(assetAccount);
        accountRepo.AddAccount(liabilityAccount);

        var service = CreateService(accountRepo: accountRepo);
        var transaction = CreateBalancedTransaction(assetAccount.Id, liabilityAccount.Id, 3000);

        await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.Equal(7000, assetAccount.Balance);
        Assert.Equal(3000, liabilityAccount.Balance);
    }

    [Fact]
    public async Task PostTransactionAsync_InsufficientFunds_RejectsTransaction()
    {
        var assetAccount = CreateAssetAccount(1000); // Only 1000 available
        var liabilityAccount = CreateLiabilityAccount();
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(assetAccount);
        accountRepo.AddAccount(liabilityAccount);
        var ledgerRepo = new FakeLedgerEntryRepository();

        var service = CreateService(accountRepo: accountRepo, ledgerRepo: ledgerRepo);
        var transaction = CreateBalancedTransaction(assetAccount.Id, liabilityAccount.Id, 5000);

        var result = await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("51", result.ErrorCode);
        Assert.Contains("Insufficient funds", result.ErrorMessage!);
        // No entries should be created
        Assert.Empty(ledgerRepo.AddedEntries);
    }

    [Fact]
    public async Task PostTransactionAsync_InsufficientFunds_ExpenseAccount_Rejects()
    {
        var expenseAccount = CreateExpenseAccount(2000);
        var liabilityAccount = CreateLiabilityAccount();
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(expenseAccount);
        accountRepo.AddAccount(liabilityAccount);

        var service = CreateService(accountRepo: accountRepo);
        var transaction = CreateBalancedTransaction(expenseAccount.Id, liabilityAccount.Id, 5000);

        var result = await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("51", result.ErrorCode);
    }

    [Fact]
    public async Task PostTransactionAsync_LiabilityAccountCanGoNegative()
    {
        var liabilityAccount = CreateLiabilityAccount(0);
        var assetAccount = CreateAssetAccount(10000);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(liabilityAccount);
        accountRepo.AddAccount(assetAccount);

        var service = CreateService(accountRepo: accountRepo);
        // Debit liability (can go negative), credit asset
        var transaction = CreateBalancedTransaction(liabilityAccount.Id, assetAccount.Id, 5000);

        var result = await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(-5000, liabilityAccount.Balance);
    }

    [Fact]
    public async Task PostTransactionAsync_UnbalancedTransaction_ReturnsFailure()
    {
        var assetAccount = CreateAssetAccount(10000);
        var liabilityAccount = CreateLiabilityAccount();
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(assetAccount);
        accountRepo.AddAccount(liabilityAccount);

        var service = CreateService(accountRepo: accountRepo);
        var transaction = new LedgerTransaction
        {
            Entries = new List<LedgerTransactionEntry>
            {
                new() { AccountId = assetAccount.Id, EntryType = EntryType.Debit, Amount = 5000 },
                new() { AccountId = liabilityAccount.Id, EntryType = EntryType.Credit, Amount = 3000 }
            },
            OperationIdentifier = "TXN-002",
            Description = "Unbalanced"
        };

        var result = await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
        Assert.Contains("not balanced", result.ErrorMessage!);
    }

    [Fact]
    public async Task PostTransactionAsync_NoEntries_ReturnsFailure()
    {
        var service = CreateService();
        var transaction = new LedgerTransaction
        {
            Entries = new List<LedgerTransactionEntry>(),
            OperationIdentifier = "TXN-003",
            Description = "Empty"
        };

        var result = await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
    }

    [Fact]
    public async Task PostTransactionAsync_OnlyDebits_ReturnsFailure()
    {
        var assetAccount = CreateAssetAccount(10000);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(assetAccount);

        var service = CreateService(accountRepo: accountRepo);
        var transaction = new LedgerTransaction
        {
            Entries = new List<LedgerTransactionEntry>
            {
                new() { AccountId = assetAccount.Id, EntryType = EntryType.Debit, Amount = 5000 }
            },
            OperationIdentifier = "TXN-004",
            Description = "Only debits"
        };

        var result = await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
    }

    [Fact]
    public async Task PostTransactionAsync_ZeroAmount_ReturnsFailure()
    {
        var assetAccount = CreateAssetAccount(10000);
        var liabilityAccount = CreateLiabilityAccount();
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(assetAccount);
        accountRepo.AddAccount(liabilityAccount);

        var service = CreateService(accountRepo: accountRepo);
        var transaction = new LedgerTransaction
        {
            Entries = new List<LedgerTransactionEntry>
            {
                new() { AccountId = assetAccount.Id, EntryType = EntryType.Debit, Amount = 0 },
                new() { AccountId = liabilityAccount.Id, EntryType = EntryType.Credit, Amount = 0 }
            },
            OperationIdentifier = "TXN-005",
            Description = "Zero amounts"
        };

        var result = await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
    }

    [Fact]
    public async Task PostTransactionAsync_AccountNotFound_ReturnsFailure()
    {
        var assetAccount = CreateAssetAccount(10000);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(assetAccount);

        var service = CreateService(accountRepo: accountRepo);
        var missingAccountId = Guid.NewGuid();
        var transaction = CreateBalancedTransaction(assetAccount.Id, missingAccountId, 1000);

        var result = await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.ErrorMessage!);
    }

    [Fact]
    public async Task PostTransactionAsync_LockTimeout_ReturnsFailure()
    {
        var assetAccount = CreateAssetAccount(10000);
        var liabilityAccount = CreateLiabilityAccount();
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(assetAccount);
        accountRepo.AddAccount(liabilityAccount);
        accountRepo.ThrowOnForUpdate = new Exception("lock_not_available: could not obtain lock on row");

        var service = CreateService(accountRepo: accountRepo);
        var transaction = CreateBalancedTransaction(assetAccount.Id, liabilityAccount.Id, 5000);

        var result = await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("96", result.ErrorCode);
        Assert.Contains("Lock timeout", result.ErrorMessage!);
    }

    [Fact]
    public async Task PostTransactionAsync_SetsLockTimeout()
    {
        var assetAccount = CreateAssetAccount(10000);
        var liabilityAccount = CreateLiabilityAccount();
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(assetAccount);
        accountRepo.AddAccount(liabilityAccount);
        var uow = new FakeUnitOfWork();

        var service = CreateService(accountRepo: accountRepo, unitOfWork: uow);
        var transaction = CreateBalancedTransaction(assetAccount.Id, liabilityAccount.Id, 1000);

        await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.Contains("SET LOCAL lock_timeout = '5s'", uow.ExecutedSql);
    }

    [Fact]
    public async Task PostTransactionAsync_CommitsOnSuccess()
    {
        var assetAccount = CreateAssetAccount(10000);
        var liabilityAccount = CreateLiabilityAccount();
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(assetAccount);
        accountRepo.AddAccount(liabilityAccount);
        var uow = new FakeUnitOfWork();

        var service = CreateService(accountRepo: accountRepo, unitOfWork: uow);
        var transaction = CreateBalancedTransaction(assetAccount.Id, liabilityAccount.Id, 1000);

        await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.True(uow.WasCommitted);
        Assert.False(uow.WasRolledBack);
    }

    [Fact]
    public async Task PostTransactionAsync_RollsBackOnInsufficientFunds()
    {
        var assetAccount = CreateAssetAccount(100);
        var liabilityAccount = CreateLiabilityAccount();
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(assetAccount);
        accountRepo.AddAccount(liabilityAccount);
        var uow = new FakeUnitOfWork();

        var service = CreateService(accountRepo: accountRepo, unitOfWork: uow);
        var transaction = CreateBalancedTransaction(assetAccount.Id, liabilityAccount.Id, 5000);

        await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.True(uow.WasRolledBack);
        Assert.False(uow.WasCommitted);
    }

    [Fact]
    public async Task PostTransactionAsync_EntriesAreImmutableWithUtcTimestamps()
    {
        var assetAccount = CreateAssetAccount(10000);
        var liabilityAccount = CreateLiabilityAccount();
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(assetAccount);
        accountRepo.AddAccount(liabilityAccount);
        var ledgerRepo = new FakeLedgerEntryRepository();

        var service = CreateService(accountRepo: accountRepo, ledgerRepo: ledgerRepo);
        var transaction = CreateBalancedTransaction(assetAccount.Id, liabilityAccount.Id, 2000);

        await service.PostTransactionAsync(transaction, CancellationToken.None);

        foreach (var entry in ledgerRepo.AddedEntries)
        {
            Assert.Equal(DateTimeKind.Utc, entry.CreatedAtUtc.Kind);
            Assert.Equal(0, entry.CreatedAtUtc.Ticks % TimeSpan.TicksPerMillisecond);
        }
    }

    [Fact]
    public async Task PostTransactionAsync_MissingOperationIdentifier_ReturnsFailure()
    {
        var service = CreateService();
        var transaction = new LedgerTransaction
        {
            Entries = new List<LedgerTransactionEntry>
            {
                new() { AccountId = Guid.NewGuid(), EntryType = EntryType.Debit, Amount = 1000 },
                new() { AccountId = Guid.NewGuid(), EntryType = EntryType.Credit, Amount = 1000 }
            },
            OperationIdentifier = "",
            Description = "Missing op id"
        };

        var result = await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
    }

    [Fact]
    public async Task PostTransactionAsync_MissingDescription_ReturnsFailure()
    {
        var service = CreateService();
        var transaction = new LedgerTransaction
        {
            Entries = new List<LedgerTransactionEntry>
            {
                new() { AccountId = Guid.NewGuid(), EntryType = EntryType.Debit, Amount = 1000 },
                new() { AccountId = Guid.NewGuid(), EntryType = EntryType.Credit, Amount = 1000 }
            },
            OperationIdentifier = "TXN-006",
            Description = ""
        };

        var result = await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
    }

    [Fact]
    public async Task GetBalanceAsync_ExistingAccount_ReturnsBalance()
    {
        var account = CreateAssetAccount(7500);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(account);

        var service = CreateService(accountRepo: accountRepo);

        var result = await service.GetBalanceAsync(account.Id, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(7500, result.Value!.Balance);
        Assert.Equal("NGN", result.Value.Currency);
        Assert.Equal(account.Id, result.Value.AccountId);
    }

    [Fact]
    public async Task GetBalanceAsync_NonExistentAccount_ReturnsFailure()
    {
        var service = CreateService();

        var result = await service.GetBalanceAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.ErrorMessage!);
    }

    [Fact]
    public async Task ReverseTransactionAsync_ExistingTransaction_CreatesOffsettingEntries()
    {
        var assetAccount = CreateAssetAccount(10000);
        var liabilityAccount = CreateLiabilityAccount(5000);
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(assetAccount);
        accountRepo.AddAccount(liabilityAccount);
        var ledgerRepo = new FakeLedgerEntryRepository();
        var transactionRepo = new FakeTransactionRepository();

        // Set up original transaction with entries
        var originalTxnId = Guid.NewGuid();
        var stan = "123456";
        var originalRecord = TransactionRecord.Create(
            stan, "0100", Guid.NewGuid(), 5000, "NGN",
            CardManagement.Domain.ValueObjects.ProcessorType.Interswitch);
        transactionRepo.AddTransaction(originalRecord);

        // Add original entries to ledger repo
        var originalDebit = LedgerEntry.Create(
            originalRecord.Id, assetAccount.Id, EntryType.Debit, 5000, "Original", stan);
        var originalCredit = LedgerEntry.Create(
            originalRecord.Id, liabilityAccount.Id, EntryType.Credit, 5000, "Original", stan);
        ledgerRepo.AddEntriesForTransaction(originalRecord.Id, new[] { originalDebit, originalCredit });

        var service = CreateService(accountRepo: accountRepo, ledgerRepo: ledgerRepo, transactionRepo: transactionRepo);

        var result = await service.ReverseTransactionAsync(stan, CancellationToken.None);

        Assert.True(result.IsSuccess);
        // Reversal should create 2 new entries (offsetting)
        Assert.Equal(2, ledgerRepo.AddedEntries.Count);
        // Original was debit on asset → reversal should credit asset
        Assert.Contains(ledgerRepo.AddedEntries,
            e => e.AccountId == assetAccount.Id && e.EntryType == EntryType.Credit && e.Amount == 5000);
        // Original was credit on liability → reversal should debit liability
        Assert.Contains(ledgerRepo.AddedEntries,
            e => e.AccountId == liabilityAccount.Id && e.EntryType == EntryType.Debit && e.Amount == 5000);
    }

    [Fact]
    public async Task ReverseTransactionAsync_OriginalNotFound_ReturnsFailure()
    {
        var service = CreateService();

        var result = await service.ReverseTransactionAsync("999999", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Contains("not found", result.ErrorMessage!);
    }

    [Fact]
    public async Task ReverseTransactionAsync_EmptyStan_ReturnsFailure()
    {
        var service = CreateService();

        var result = await service.ReverseTransactionAsync("", CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
    }

    [Fact]
    public async Task PostTransactionAsync_MultipleEntriesBalanced_Succeeds()
    {
        var asset1 = CreateAssetAccount(10000);
        var asset2 = Account.Create("ACC-004", AccountType.Asset, "NGN", 10000);
        var liability = CreateLiabilityAccount();
        var accountRepo = new FakeAccountRepository();
        accountRepo.AddAccount(asset1);
        accountRepo.AddAccount(asset2);
        accountRepo.AddAccount(liability);
        var ledgerRepo = new FakeLedgerEntryRepository();

        var service = CreateService(accountRepo: accountRepo, ledgerRepo: ledgerRepo);
        var transaction = new LedgerTransaction
        {
            Entries = new List<LedgerTransactionEntry>
            {
                new() { AccountId = asset1.Id, EntryType = EntryType.Debit, Amount = 3000 },
                new() { AccountId = asset2.Id, EntryType = EntryType.Debit, Amount = 2000 },
                new() { AccountId = liability.Id, EntryType = EntryType.Credit, Amount = 5000 }
            },
            OperationIdentifier = "TXN-MULTI",
            Description = "Multi-entry transaction"
        };

        var result = await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, ledgerRepo.AddedEntries.Count);
        Assert.Equal(7000, asset1.Balance);
        Assert.Equal(8000, asset2.Balance);
        Assert.Equal(5000, liability.Balance);
    }

    [Fact]
    public async Task PostTransactionAsync_NegativeAmount_ReturnsFailure()
    {
        var service = CreateService();
        var transaction = new LedgerTransaction
        {
            Entries = new List<LedgerTransactionEntry>
            {
                new() { AccountId = Guid.NewGuid(), EntryType = EntryType.Debit, Amount = -1000 },
                new() { AccountId = Guid.NewGuid(), EntryType = EntryType.Credit, Amount = -1000 }
            },
            OperationIdentifier = "TXN-NEG",
            Description = "Negative amounts"
        };

        var result = await service.PostTransactionAsync(transaction, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("30", result.ErrorCode);
    }

    #region Fake Implementations

    private class FakeAccountRepository : IAccountRepository
    {
        private readonly Dictionary<Guid, Account> _accounts = new();
        public Exception? ThrowOnForUpdate { get; set; }

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
            if (ThrowOnForUpdate is not null)
                throw ThrowOnForUpdate;

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
        private readonly Dictionary<Guid, IReadOnlyList<LedgerEntry>> _entriesByTransaction = new();

        public void AddEntriesForTransaction(Guid transactionId, IEnumerable<LedgerEntry> entries)
        {
            _entriesByTransaction[transactionId] = entries.ToList();
        }

        public Task AddRangeAsync(IEnumerable<LedgerEntry> entries, CancellationToken cancellationToken = default)
        {
            AddedEntries.AddRange(entries);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LedgerEntry>> GetByTransactionIdAsync(Guid transactionId, CancellationToken cancellationToken = default)
        {
            if (_entriesByTransaction.TryGetValue(transactionId, out var entries))
                return Task.FromResult(entries);

            return Task.FromResult<IReadOnlyList<LedgerEntry>>(Array.Empty<LedgerEntry>());
        }
    }

    private class FakeTransactionRepository : ITransactionRepository
    {
        private readonly Dictionary<string, TransactionRecord> _transactions = new();

        public void AddTransaction(TransactionRecord record)
        {
            _transactions[record.SystemTraceAuditNumber] = record;
        }

        public Task AddAsync(TransactionRecord transaction, CancellationToken cancellationToken = default)
        {
            _transactions[transaction.SystemTraceAuditNumber] = transaction;
            return Task.CompletedTask;
        }

        public Task<TransactionRecord?> GetByStanAsync(string systemTraceAuditNumber, CancellationToken cancellationToken = default)
        {
            _transactions.TryGetValue(systemTraceAuditNumber, out var record);
            return Task.FromResult(record);
        }

        public Task UpdateAsync(TransactionRecord transaction, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private class FakeUnitOfWork : IUnitOfWork
    {
        public List<string> ExecutedSql { get; } = new();
        public bool WasCommitted { get; private set; }
        public bool WasRolledBack { get; private set; }
        public bool WasBegun { get; private set; }

        public Task BeginTransactionAsync(CancellationToken ct = default)
        {
            WasBegun = true;
            return Task.CompletedTask;
        }

        public Task ExecuteSqlAsync(string sql, CancellationToken ct = default)
        {
            ExecutedSql.Add(sql);
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken ct = default)
        {
            WasCommitted = true;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken ct = default)
        {
            WasRolledBack = true;
            return Task.CompletedTask;
        }

        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    #endregion
}
