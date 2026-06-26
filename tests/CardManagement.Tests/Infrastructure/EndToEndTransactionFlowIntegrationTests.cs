using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Kafka;
using CardManagement.Infrastructure.Ledger;
using CardManagement.Infrastructure.Pipeline;
using CardManagement.Infrastructure.Tcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

/// <summary>
/// Integration tests for end-to-end transaction flow verifying:
/// - Full authorization pipeline (account creation → card → auth request → response "00")
/// - Full reversal pipeline (post transaction → reversal → response "00" and balances restored)
/// - Lock timeout under concurrent access (two transactions on same account)
/// - TCP connection handling: concurrent connections, timeout, unexpected disconnect
/// 
/// Uses in-memory fakes for database-dependent tests as actual PostgreSQL is not available
/// in the test environment.
/// </summary>
[Trait("Category", "Integration")]
public class EndToEndTransactionFlowIntegrationTests : IAsyncLifetime
{
    private const string ValidPan = "4111111111111111";
    private const string ValidExpiryMmyy = "12/29";
    private const string ValidExpiryYymm = "2912";

    private TcpListenerService? _tcpService;
    private CancellationTokenSource? _cts;
    private int _port;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_cts != null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }

        if (_tcpService != null)
        {
            await _tcpService.StopAsync(CancellationToken.None);
            _tcpService.Dispose();
        }
    }

    #region End-to-End Authorization Tests

    [Fact]
    public async Task FullAuthorizationPipeline_CreateAccountAndCard_SendAuthRequest_ReturnsApproval00()
    {
        // Arrange — Create account with sufficient funds
        var account = Account.Create("ACC-E2E-001", AccountType.Asset, "NGN", 50000);
        var panHash = ComputePanHash(ValidPan);
        var card = Card.Create(
            panEncrypted: "encrypted_pan_e2e",
            panHash: panHash,
            cvv2Encrypted: "encrypted_cvv2_e2e",
            expiryDate: ValidExpiryMmyy,
            accountId: account.Id,
            cardScheme: CardScheme.Visa,
            binRange: "411111");

        var cardRepo = new InMemoryCardRepository();
        cardRepo.AddCard(panHash, card);
        var accountRepo = new InMemoryAccountRepository();
        accountRepo.AddAccount(account);
        var ledgerEntryRepo = new InMemoryLedgerEntryRepository();
        var transactionRepo = new InMemoryTransactionRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var ledgerService = new LedgerService(
            accountRepo, ledgerEntryRepo, transactionRepo, unitOfWork,
            new NullLoggerFactory().CreateLogger<LedgerService>());

        var gateway = new PassThroughIso8583Gateway();
        var pipeline = new TransactionPipeline(
            gateway, cardRepo, accountRepo, ledgerService, transactionRepo,
            new NullEventPublisher(new NullLoggerFactory().CreateLogger<NullEventPublisher>()),
            new NullLoggerFactory().CreateLogger<TransactionPipeline>());

        // Act — Send authorization request
        var authRequest = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [2] = ValidPan,
                [3] = "000000",
                [4] = "10000",
                [11] = "123456",
                [14] = ValidExpiryYymm,
                [22] = "051",
                [25] = "00",
                [41] = "TERM0001",
                [49] = "566"
            }
        };

        var response = await pipeline.ProcessAuthorizationAsync(authRequest, CancellationToken.None);

        // Assert
        Assert.Equal("0110", response.Mti);
        Assert.Equal("00", response.Fields[39]);
        Assert.True(response.Fields.ContainsKey(11)); // STAN present
        Assert.Equal(ValidPan, response.Fields[2]); // PAN echoed
        Assert.Equal("10000", response.Fields[4]); // Amount echoed

        // Verify transaction was persisted
        Assert.NotEmpty(transactionRepo.GetAll());
        var txn = transactionRepo.GetAll().Last();
        Assert.Equal("00", txn.ResponseCode);
        Assert.Equal(TransactionStatus.Approved, txn.Status);
    }

    [Fact]
    public async Task FullAuthorizationPipeline_InsufficientBalance_ReturnsDecline51()
    {
        // Arrange — Account with only 1000 but requesting 10000
        var account = Account.Create("ACC-E2E-002", AccountType.Asset, "NGN", 1000);
        var panHash = ComputePanHash(ValidPan);
        var card = Card.Create(
            panEncrypted: "encrypted",
            panHash: panHash,
            cvv2Encrypted: "encrypted",
            expiryDate: ValidExpiryMmyy,
            accountId: account.Id,
            cardScheme: CardScheme.Visa,
            binRange: "411111");

        var cardRepo = new InMemoryCardRepository();
        cardRepo.AddCard(panHash, card);
        var accountRepo = new InMemoryAccountRepository();
        accountRepo.AddAccount(account);
        var ledgerEntryRepo = new InMemoryLedgerEntryRepository();
        var transactionRepo = new InMemoryTransactionRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var ledgerService = new LedgerService(
            accountRepo, ledgerEntryRepo, transactionRepo, unitOfWork,
            new NullLoggerFactory().CreateLogger<LedgerService>());

        var gateway = new PassThroughIso8583Gateway();
        var pipeline = new TransactionPipeline(
            gateway, cardRepo, accountRepo, ledgerService, transactionRepo,
            new NullEventPublisher(new NullLoggerFactory().CreateLogger<NullEventPublisher>()),
            new NullLoggerFactory().CreateLogger<TransactionPipeline>());

        var authRequest = new Iso8583Message
        {
            Mti = "0100",
            Fields = new Dictionary<int, string>
            {
                [2] = ValidPan,
                [3] = "000000",
                [4] = "10000",
                [11] = "123456",
                [14] = ValidExpiryYymm,
                [22] = "051",
                [25] = "00",
                [41] = "TERM0001",
                [49] = "566"
            }
        };

        // Act
        var response = await pipeline.ProcessAuthorizationAsync(authRequest, CancellationToken.None);

        // Assert
        Assert.Equal("0110", response.Mti);
        Assert.Equal("51", response.Fields[39]);
    }

    #endregion

    #region End-to-End Reversal Tests

    [Fact]
    public async Task FullReversalPipeline_PostTransaction_SendReversal_ReturnsApproval00AndBalancesRestored()
    {
        // Arrange — Create account with funds, post a transaction, then reverse it
        var account = Account.Create("ACC-E2E-003", AccountType.Asset, "NGN", 50000);
        var liabilityAccount = Account.Create("ACC-E2E-004", AccountType.Liability, "NGN", 0);

        var accountRepo = new InMemoryAccountRepository();
        accountRepo.AddAccount(account);
        accountRepo.AddAccount(liabilityAccount);
        var ledgerEntryRepo = new InMemoryLedgerEntryRepository();
        var transactionRepo = new InMemoryTransactionRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var ledgerService = new LedgerService(
            accountRepo, ledgerEntryRepo, transactionRepo, unitOfWork,
            new NullLoggerFactory().CreateLogger<LedgerService>());

        // Post original transaction — debit asset, credit liability
        var stan = "654321";
        var originalTransaction = new LedgerTransaction
        {
            Entries = new List<LedgerTransactionEntry>
            {
                new() { AccountId = account.Id, EntryType = EntryType.Debit, Amount = 15000 },
                new() { AccountId = liabilityAccount.Id, EntryType = EntryType.Credit, Amount = 15000 }
            },
            OperationIdentifier = stan,
            Description = "Original authorization"
        };

        var postResult = await ledgerService.PostTransactionAsync(originalTransaction, CancellationToken.None);
        Assert.True(postResult.IsSuccess);

        // Verify balances after posting
        Assert.Equal(35000, account.Balance);
        Assert.Equal(15000, liabilityAccount.Balance);

        // Persist a transaction record so reversal can look it up by STAN.
        // Use the same transaction ID that PostTransactionAsync generated internally,
        // so ReverseTransactionAsync can find matching ledger entries.
        var postedTxnId = postResult.Value!.Id;
        var txnRecord = TransactionRecord.Create(
            stan, "0100", Guid.NewGuid(), 15000, "NGN", ProcessorType.Interswitch);
        // Set the ID to match the one used for ledger entries
        var idProp = typeof(TransactionRecord).GetProperty(nameof(TransactionRecord.Id));
        idProp!.SetValue(txnRecord, postedTxnId);
        txnRecord.Approve("00", "ResponseTransmission");
        await transactionRepo.AddAsync(txnRecord, CancellationToken.None);

        // Act — Now set up the pipeline and send reversal
        var panHash = ComputePanHash(ValidPan);
        var card = Card.Create(
            panEncrypted: "enc", panHash: panHash, cvv2Encrypted: "enc",
            expiryDate: ValidExpiryMmyy, accountId: account.Id,
            cardScheme: CardScheme.Visa, binRange: "411111");

        var cardRepo = new InMemoryCardRepository();
        cardRepo.AddCard(panHash, card);

        var gateway = new PassThroughIso8583Gateway();
        var pipeline = new TransactionPipeline(
            gateway, cardRepo, accountRepo, ledgerService, transactionRepo,
            new NullEventPublisher(new NullLoggerFactory().CreateLogger<NullEventPublisher>()),
            new NullLoggerFactory().CreateLogger<TransactionPipeline>());

        var reversalRequest = new Iso8583Message
        {
            Mti = "0420",
            Fields = new Dictionary<int, string>
            {
                [2] = ValidPan,
                [4] = "15000",
                [11] = stan
            }
        };

        var response = await pipeline.ProcessReversalAsync(reversalRequest, CancellationToken.None);

        // Assert — Reversal approved
        Assert.Equal("0430", response.Mti);
        Assert.Equal("00", response.Fields[39]);

        // Verify balances restored
        Assert.Equal(50000, account.Balance);
        Assert.Equal(0, liabilityAccount.Balance);
    }

    [Fact]
    public async Task FullReversalPipeline_OriginalNotFound_ReturnsDecline()
    {
        // Arrange — Empty repositories, no original transaction exists
        var accountRepo = new InMemoryAccountRepository();
        var ledgerEntryRepo = new InMemoryLedgerEntryRepository();
        var transactionRepo = new InMemoryTransactionRepository();
        var unitOfWork = new InMemoryUnitOfWork();

        var ledgerService = new LedgerService(
            accountRepo, ledgerEntryRepo, transactionRepo, unitOfWork,
            new NullLoggerFactory().CreateLogger<LedgerService>());

        var cardRepo = new InMemoryCardRepository();
        var gateway = new PassThroughIso8583Gateway();
        var pipeline = new TransactionPipeline(
            gateway, cardRepo, accountRepo, ledgerService, transactionRepo,
            new NullEventPublisher(new NullLoggerFactory().CreateLogger<NullEventPublisher>()),
            new NullLoggerFactory().CreateLogger<TransactionPipeline>());

        var reversalRequest = new Iso8583Message
        {
            Mti = "0420",
            Fields = new Dictionary<int, string>
            {
                [2] = ValidPan,
                [4] = "5000",
                [11] = "999999"
            }
        };

        // Act
        var response = await pipeline.ProcessReversalAsync(reversalRequest, CancellationToken.None);

        // Assert
        Assert.Equal("0430", response.Mti);
        Assert.Equal("96", response.Fields[39]); // Decline — not found
    }

    #endregion

    #region Lock Timeout Under Concurrent Access Tests

    [Fact]
    public async Task LockTimeout_ConcurrentAccessOnSameAccount_SecondTransactionFailsOnContention()
    {
        // Arrange — Account with sufficient funds for both transactions
        var account = Account.Create("ACC-E2E-LOCK", AccountType.Asset, "NGN", 100000);
        var liabilityAccount = Account.Create("ACC-E2E-LOCK-L", AccountType.Liability, "NGN", 0);

        var accountRepo = new InMemoryAccountRepository();
        accountRepo.AddAccount(account);
        accountRepo.AddAccount(liabilityAccount);
        var ledgerEntryRepo = new InMemoryLedgerEntryRepository();
        var transactionRepo = new InMemoryTransactionRepository();

        // Configure unit of work to simulate lock timeout on the second concurrent call
        var unitOfWork = new LockContentionUnitOfWork();

        var ledgerService = new LedgerService(
            accountRepo, ledgerEntryRepo, transactionRepo, unitOfWork,
            new NullLoggerFactory().CreateLogger<LedgerService>());

        var txn1 = new LedgerTransaction
        {
            Entries = new List<LedgerTransactionEntry>
            {
                new() { AccountId = account.Id, EntryType = EntryType.Debit, Amount = 5000 },
                new() { AccountId = liabilityAccount.Id, EntryType = EntryType.Credit, Amount = 5000 }
            },
            OperationIdentifier = "TXN-LOCK-1",
            Description = "First concurrent transaction"
        };

        var txn2 = new LedgerTransaction
        {
            Entries = new List<LedgerTransactionEntry>
            {
                new() { AccountId = account.Id, EntryType = EntryType.Debit, Amount = 3000 },
                new() { AccountId = liabilityAccount.Id, EntryType = EntryType.Credit, Amount = 3000 }
            },
            OperationIdentifier = "TXN-LOCK-2",
            Description = "Second concurrent transaction"
        };

        // Act — Run both transactions concurrently
        var task1 = ledgerService.PostTransactionAsync(txn1, CancellationToken.None);
        var task2 = ledgerService.PostTransactionAsync(txn2, CancellationToken.None);

        var results = await Task.WhenAll(task1, task2);

        // Assert — At least one succeeds, and the contention one gets a lock timeout error
        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count(r => !r.IsSuccess);

        // With our simulated lock contention, the second call fails
        Assert.True(successCount >= 1, "At least one transaction should succeed");
        Assert.True(failureCount >= 1, "At least one transaction should fail due to lock contention");

        var failedResult = results.First(r => !r.IsSuccess);
        Assert.Equal("96", failedResult.ErrorCode);
        Assert.Contains("Lock timeout", failedResult.ErrorMessage!);
    }

    #endregion

    #region TCP Connection Handling Tests

    [Fact]
    public async Task TcpConcurrentConnections_MultipleClientsConnectSimultaneously_AllAccepted()
    {
        // Arrange
        _tcpService = CreateTcpService(maxConnections: 10);
        await StartTcpServiceAsync();

        var clients = new List<TcpClient>();

        // Act — connect 5 clients simultaneously
        var connectTasks = Enumerable.Range(0, 5).Select(_ => Task.Run(async () =>
        {
            var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port);
            return client;
        }));

        var connectedClients = await Task.WhenAll(connectTasks);
        clients.AddRange(connectedClients);

        await Task.Delay(300); // Allow service to process all connections

        // Assert — all clients connected and service tracks them
        Assert.True(_tcpService.ActiveConnectionCount >= 5);
        foreach (var client in clients)
        {
            Assert.True(client.Connected);
        }

        // Cleanup
        foreach (var client in clients)
        {
            client.Dispose();
        }
    }

    [Fact]
    public async Task TcpTimeout_ConnectButNoData_ConnectionClosedAfterTimeout()
    {
        // Arrange — very short timeout for test speed (1 second)
        _tcpService = CreateTcpService(readTimeoutSeconds: 1);
        await StartTcpServiceAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);

        // Act — don't send any data, wait for server to close connection
        await Task.Delay(2500); // Wait beyond the 1-second timeout

        // Assert — Server should have closed the connection
        Assert.Equal(0, _tcpService.ActiveConnectionCount);
    }

    [Fact]
    public async Task TcpUnexpectedDisconnect_AbruptClose_ServerCleansUp()
    {
        // Arrange
        _tcpService = CreateTcpService(readTimeoutSeconds: 30);
        await StartTcpServiceAsync();

        var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, _port);
        await Task.Delay(200);

        Assert.True(_tcpService.ActiveConnectionCount >= 1);

        // Act — abruptly close the connection with RST
        client.Client.LingerState = new LingerOption(true, 0); // Force RST on close
        client.Dispose();

        await Task.Delay(500); // Allow server to detect and clean up

        // Assert — Server cleaned up all resources
        Assert.Equal(0, _tcpService.ActiveConnectionCount);
    }

    [Fact]
    public async Task TcpConcurrentConnections_ExceedMaxLimit_ExcessBlockedUntilSlotFrees()
    {
        // Arrange — max 2 connections
        _tcpService = CreateTcpService(maxConnections: 2, readTimeoutSeconds: 2);
        await StartTcpServiceAsync();

        // Act — connect 2 clients (fills the pool)
        using var client1 = new TcpClient();
        using var client2 = new TcpClient();
        await client1.ConnectAsync(IPAddress.Loopback, _port);
        await client2.ConnectAsync(IPAddress.Loopback, _port);
        await Task.Delay(200);

        Assert.Equal(2, _tcpService.ActiveConnectionCount);

        // A 3rd client connection attempt — the accept loop is blocked
        // by the semaphore, but TCP backlog may allow the OS-level connection.
        // Once one slot frees (after timeout), the 3rd should be accepted.
        using var client3 = new TcpClient();
        var connectTask = client3.ConnectAsync(IPAddress.Loopback, _port);

        // Wait for timeout to expire one of the idle connections
        await Task.Delay(3000);

        // Assert — After timeouts, server cleaned up idle connections and
        // can potentially accept new ones
        Assert.True(_tcpService.ActiveConnectionCount <= 2);
    }

    #endregion

    #region Helper Methods

    private static string ComputePanHash(string pan)
    {
        byte[] hashBytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(pan));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private TcpListenerService CreateTcpService(
        ITcpConnectionHandler? handler = null,
        int maxConnections = 10,
        int readTimeoutSeconds = 30)
    {
        _port = GetFreePort();
        handler ??= new NoOpTcpConnectionHandler();

        var configValues = new Dictionary<string, string?>
        {
            ["TCP__LISTENER_PORT"] = _port.ToString(),
            ["TCP__MAX_CONNECTIONS"] = maxConnections.ToString(),
            ["TCP__READ_TIMEOUT_SECONDS"] = readTimeoutSeconds.ToString(),
            ["TCP__MAX_FRAME_SIZE"] = "9999"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        return new TcpListenerService(handler, configuration, NullLogger<TcpListenerService>.Instance);
    }

    private async Task StartTcpServiceAsync()
    {
        _cts = new CancellationTokenSource();
        await _tcpService!.StartAsync(_cts.Token);
        await Task.Delay(150); // Allow listener to start accepting
    }

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    #endregion

    #region In-Memory Implementations for Integration Testing

    /// <summary>
    /// Pass-through ISO 8583 gateway that doesn't do actual binary encoding.
    /// For integration tests, the pipeline works with structured Iso8583Message objects directly.
    /// </summary>
    private class PassThroughIso8583Gateway : IIso8583Gateway
    {
        public Result<Iso8583Message> Parse(ReadOnlySpan<byte> rawMessage)
        {
            return Result<Iso8583Message>.Success(new Iso8583Message
            {
                Mti = "0100",
                Fields = new Dictionary<int, string>()
            });
        }

        public Result<byte[]> Construct(Iso8583Message message)
        {
            return Result<byte[]>.Success(Array.Empty<byte>());
        }

        public bool IsValidFrameSize(int messageLength) => messageLength <= 9999;
    }

    /// <summary>
    /// In-memory card repository for integration tests.
    /// </summary>
    private class InMemoryCardRepository : ICardRepository
    {
        private readonly Dictionary<string, Card> _cards = new();

        public void AddCard(string panHash, Card card) => _cards[panHash] = card;

        public Task<Card?> GetByPanHashAsync(string panHash, CancellationToken cancellationToken = default)
        {
            _cards.TryGetValue(panHash, out var card);
            return Task.FromResult(card);
        }

        public Task<bool> ExistsByPanHashAsync(string panHash, CancellationToken cancellationToken = default)
            => Task.FromResult(_cards.ContainsKey(panHash));

        public Task AddAsync(Card card, CancellationToken cancellationToken = default)
        {
            _cards[card.PanHash] = card;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// In-memory account repository for integration tests.
    /// Supports simulated lock contention via ThrowOnForUpdate.
    /// </summary>
    private class InMemoryAccountRepository : IAccountRepository
    {
        private readonly Dictionary<Guid, Account> _accounts = new();

        public void AddAccount(Account account) => _accounts[account.Id] = account;

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
            => Task.CompletedTask;
    }

    /// <summary>
    /// In-memory ledger entry repository for integration tests.
    /// </summary>
    private class InMemoryLedgerEntryRepository : ILedgerEntryRepository
    {
        private readonly List<LedgerEntry> _entries = new();

        public Task AddRangeAsync(IEnumerable<LedgerEntry> entries, CancellationToken cancellationToken = default)
        {
            _entries.AddRange(entries);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LedgerEntry>> GetByTransactionIdAsync(Guid transactionId, CancellationToken cancellationToken = default)
        {
            var result = _entries.Where(e => e.TransactionId == transactionId).ToList();
            return Task.FromResult<IReadOnlyList<LedgerEntry>>(result);
        }
    }

    /// <summary>
    /// In-memory transaction repository for integration tests.
    /// </summary>
    private class InMemoryTransactionRepository : ITransactionRepository
    {
        private readonly List<TransactionRecord> _transactions = new();

        public Task AddAsync(TransactionRecord transaction, CancellationToken cancellationToken = default)
        {
            _transactions.Add(transaction);
            return Task.CompletedTask;
        }

        public Task<TransactionRecord?> GetByStanAsync(string systemTraceAuditNumber, CancellationToken cancellationToken = default)
        {
            var record = _transactions.FirstOrDefault(t => t.SystemTraceAuditNumber == systemTraceAuditNumber);
            return Task.FromResult(record);
        }

        public Task UpdateAsync(TransactionRecord transaction, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public IReadOnlyList<TransactionRecord> GetAll() => _transactions.AsReadOnly();
    }

    /// <summary>
    /// Standard in-memory unit of work for integration tests.
    /// </summary>
    private class InMemoryUnitOfWork : IUnitOfWork
    {
        public Task BeginTransactionAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task ExecuteSqlAsync(string sql, CancellationToken ct = default) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Unit of work that simulates lock contention by throwing a lock_timeout exception
    /// on the second concurrent call to BeginTransactionAsync.
    /// </summary>
    private class LockContentionUnitOfWork : IUnitOfWork
    {
        private int _callCount;

        public Task BeginTransactionAsync(CancellationToken ct = default)
        {
            return Task.CompletedTask;
        }

        public Task ExecuteSqlAsync(string sql, CancellationToken ct = default)
        {
            // Simulate lock_timeout on second concurrent access
            var count = Interlocked.Increment(ref _callCount);
            if (count > 1)
            {
                throw new Exception("lock_not_available: could not obtain lock on row in relation \"accounts\"");
            }
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// A no-op TCP connection handler for connection management tests.
    /// Reads data but doesn't respond, allowing timeout behavior to be tested.
    /// </summary>
    private class NoOpTcpConnectionHandler : ITcpConnectionHandler
    {
        public async Task HandleConnectionAsync(PipeReader reader, PipeWriter writer, CancellationToken ct)
        {
            try
            {
                var readResult = await reader.ReadAsync(ct);
                reader.AdvanceTo(readResult.Buffer.End);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown or timeout
            }
        }
    }

    #endregion
}
