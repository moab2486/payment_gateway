using CardManagement.Domain.Entities;
using CardManagement.Infrastructure.Audit;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CardManagement.Tests.Infrastructure;

public class AuditStoreTests : IDisposable
{
    private readonly CardManagementDbContext _dbContext;
    private readonly AuditStore _store;

    public AuditStoreTests()
    {
        var options = new DbContextOptionsBuilder<CardManagementDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new CardManagementDbContext(options);
        _store = new AuditStore(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }

    [Fact]
    public async Task AppendAsync_FirstEntry_HasNullPreviousHash()
    {
        // Arrange
        var entry = AuditEntry.Create(
            transactionReference: "TX-001",
            actorIdentity: "system",
            action: "Created",
            previousState: null,
            newState: "Created",
            correlationId: "corr-001",
            previousEntryHash: null);

        // Act
        await _store.AppendAsync(entry, CancellationToken.None);

        // Assert
        var stored = await _dbContext.AuditEntries.FirstAsync();
        Assert.Null(stored.PreviousEntryHash);
        Assert.NotNull(stored.EntryHash);
        Assert.NotEmpty(stored.EntryHash);
    }

    [Fact]
    public async Task AppendAsync_SecondEntry_LinksToFirstEntryHash()
    {
        // Arrange
        var first = AuditEntry.Create(
            transactionReference: "TX-002",
            actorIdentity: "system",
            action: "Created",
            previousState: null,
            newState: "Created",
            correlationId: "corr-002",
            previousEntryHash: null);

        await _store.AppendAsync(first, CancellationToken.None);
        var firstStored = await _dbContext.AuditEntries.FirstAsync();

        var second = AuditEntry.Create(
            transactionReference: "TX-002",
            actorIdentity: "system",
            action: "Authorized",
            previousState: "Created",
            newState: "Authorized",
            correlationId: "corr-002",
            previousEntryHash: null); // Will be corrected by the store

        // Act
        await _store.AppendAsync(second, CancellationToken.None);

        // Assert
        var entries = await _dbContext.AuditEntries
            .OrderBy(e => e.Id)
            .ToListAsync();

        Assert.Equal(2, entries.Count);
        Assert.Equal(firstStored.EntryHash, entries[1].PreviousEntryHash);
    }

    [Fact]
    public async Task AppendAsync_HashChainIsConsistent()
    {
        // Arrange & Act — Append 3 entries for same transaction
        var txRef = "TX-CHAIN-001";
        var actions = new[] { "Created", "Authorized", "Captured" };
        var states = new[] { (null as string, "Created"), ("Created", "Authorized"), ("Authorized", "Captured") };

        for (int i = 0; i < actions.Length; i++)
        {
            var entry = AuditEntry.Create(
                transactionReference: txRef,
                actorIdentity: "system",
                action: actions[i],
                previousState: states[i].Item1,
                newState: states[i].Item2,
                correlationId: "corr-chain-001",
                previousEntryHash: null);

            await _store.AppendAsync(entry, CancellationToken.None);
        }

        // Assert — verify hash chain
        var entries = await _dbContext.AuditEntries
            .OrderBy(e => e.Id)
            .ToListAsync();

        Assert.Equal(3, entries.Count);
        Assert.Null(entries[0].PreviousEntryHash); // First entry has no predecessor
        Assert.Equal(entries[0].EntryHash, entries[1].PreviousEntryHash);
        Assert.Equal(entries[1].EntryHash, entries[2].PreviousEntryHash);

        // Verify each hash matches recomputation
        foreach (var entry in entries)
        {
            Assert.Equal(entry.ComputeHash(), entry.EntryHash);
        }
    }

    [Fact]
    public async Task AppendAsync_EntriesIncludeAllRequiredFields()
    {
        // Arrange
        var entry = AuditEntry.Create(
            transactionReference: "TX-FIELDS-001",
            actorIdentity: "user@bank.com",
            action: "Reversed",
            previousState: "Captured",
            newState: "Reversed",
            correlationId: "corr-fields-001",
            previousEntryHash: null);

        // Act
        await _store.AppendAsync(entry, CancellationToken.None);

        // Assert
        var stored = await _dbContext.AuditEntries.FirstAsync();
        Assert.Equal("TX-FIELDS-001", stored.TransactionReference);
        Assert.True(stored.TimestampUtc <= DateTime.UtcNow);
        Assert.True(stored.TimestampUtc > DateTime.UtcNow.AddSeconds(-5));
        Assert.Equal("user@bank.com", stored.ActorIdentity);
        Assert.Equal("Reversed", stored.Action);
        Assert.Equal("Captured", stored.PreviousState);
        Assert.Equal("Reversed", stored.NewState);
        Assert.Equal("corr-fields-001", stored.CorrelationId);
        Assert.NotEmpty(stored.EntryHash);
    }

    [Fact]
    public async Task AppendAsync_EntriesAreImmutable_CannotModify()
    {
        // Arrange
        var entry = AuditEntry.Create(
            transactionReference: "TX-IMMUTABLE-001",
            actorIdentity: "system",
            action: "Created",
            previousState: null,
            newState: "Created",
            correlationId: "corr-immutable-001",
            previousEntryHash: null);

        await _store.AppendAsync(entry, CancellationToken.None);

        // Act & Assert — attempt to modify should throw
        var stored = await _dbContext.AuditEntries.FirstAsync();
        _dbContext.Entry(stored).State = EntityState.Modified;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task AppendAsync_EntriesAreImmutable_CannotDelete()
    {
        // Arrange
        var entry = AuditEntry.Create(
            transactionReference: "TX-NODELETE-001",
            actorIdentity: "system",
            action: "Created",
            previousState: null,
            newState: "Created",
            correlationId: "corr-nodelete-001",
            previousEntryHash: null);

        await _store.AppendAsync(entry, CancellationToken.None);

        // Act & Assert — attempt to delete should throw
        var stored = await _dbContext.AuditEntries.FirstAsync();
        _dbContext.Entry(stored).State = EntityState.Deleted;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task GetByTransactionReferenceAsync_ReturnsEntriesInOrder()
    {
        // Arrange
        var txRef = "TX-QUERY-001";
        var otherTxRef = "TX-OTHER-001";

        var entry1 = AuditEntry.Create(txRef, "system", "Created", null, "Created", "corr-001", null);
        var entry2 = AuditEntry.Create(txRef, "system", "Authorized", "Created", "Authorized", "corr-001", null);
        var otherEntry = AuditEntry.Create(otherTxRef, "system", "Created", null, "Created", "corr-002", null);

        await _store.AppendAsync(entry1, CancellationToken.None);
        await _store.AppendAsync(otherEntry, CancellationToken.None);
        await _store.AppendAsync(entry2, CancellationToken.None);

        // Act
        var results = await _store.GetByTransactionReferenceAsync(txRef, CancellationToken.None);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal("Created", results[0].Action);
        Assert.Equal("Authorized", results[1].Action);
    }

    [Fact]
    public async Task GetByTransactionReferenceAsync_NoEntries_ReturnsEmpty()
    {
        // Act
        var results = await _store.GetByTransactionReferenceAsync("TX-NONEXISTENT", CancellationToken.None);

        // Assert
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetByTransactionReferenceAsync_EmptyReference_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.GetByTransactionReferenceAsync("", CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(
            () => _store.GetByTransactionReferenceAsync("   ", CancellationToken.None));
    }
}

public class DeferredAuditQueueTests : IDisposable
{
    private readonly CardManagementDbContext _dbContext;
    private readonly AuditStore _innerStore;
    private readonly DeferredAuditQueue _deferredQueue;
    private readonly ILogger<DeferredAuditQueue> _logger;

    public DeferredAuditQueueTests()
    {
        var options = new DbContextOptionsBuilder<CardManagementDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new CardManagementDbContext(options);
        _innerStore = new AuditStore(_dbContext);
        _logger = NullLogger<DeferredAuditQueue>.Instance;
        _deferredQueue = new DeferredAuditQueue(_innerStore, _logger, TimeSpan.FromMinutes(10));
    }

    public void Dispose()
    {
        _deferredQueue.Dispose();
        _dbContext.Dispose();
    }

    [Fact]
    public async Task AppendAsync_WhenStoreAvailable_WritesDirectly()
    {
        // Arrange
        var entry = AuditEntry.Create(
            "TX-DIRECT-001", "system", "Created", null, "Created", "corr-d-001", null);

        // Act
        await _deferredQueue.AppendAsync(entry, CancellationToken.None);

        // Assert
        Assert.Equal(0, _deferredQueue.PendingCount);
        var stored = await _dbContext.AuditEntries.CountAsync();
        Assert.Equal(1, stored);
    }

    [Fact]
    public async Task AppendAsync_WhenStoreUnavailable_QueuesEntry()
    {
        // Arrange — use a failing store
        var failingStore = new FailingAuditStore();
        using var queue = new DeferredAuditQueue(failingStore, _logger, TimeSpan.FromMinutes(10));

        var entry = AuditEntry.Create(
            "TX-QUEUE-001", "system", "Created", null, "Created", "corr-q-001", null);

        // Act
        await queue.AppendAsync(entry, CancellationToken.None);

        // Assert
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public async Task FlushAsync_WhenStoreRecovery_FlushesQueuedEntries()
    {
        // Arrange — use a store that fails then recovers
        var recoveringStore = new RecoveringAuditStore(_innerStore);
        using var queue = new DeferredAuditQueue(recoveringStore, _logger, TimeSpan.FromMinutes(10));

        recoveringStore.ShouldFail = true;

        var entry1 = AuditEntry.Create(
            "TX-FLUSH-001", "system", "Created", null, "Created", "corr-f-001", null);
        var entry2 = AuditEntry.Create(
            "TX-FLUSH-001", "system", "Authorized", "Created", "Authorized", "corr-f-001", null);

        await queue.AppendAsync(entry1, CancellationToken.None);
        await queue.AppendAsync(entry2, CancellationToken.None);
        Assert.Equal(2, queue.PendingCount);

        // Act — recover and flush
        recoveringStore.ShouldFail = false;
        var flushed = await queue.FlushAsync(CancellationToken.None);

        // Assert
        Assert.Equal(2, flushed);
        Assert.Equal(0, queue.PendingCount);
        var stored = await _dbContext.AuditEntries.CountAsync();
        Assert.Equal(2, stored);
    }

    [Fact]
    public async Task FlushAsync_WhenStoreStillUnavailable_RequeuesEntries()
    {
        // Arrange
        var failingStore = new FailingAuditStore();
        using var queue = new DeferredAuditQueue(failingStore, _logger, TimeSpan.FromMinutes(10));

        var entry = AuditEntry.Create(
            "TX-REQUEUE-001", "system", "Created", null, "Created", "corr-rq-001", null);

        await queue.AppendAsync(entry, CancellationToken.None);
        Assert.Equal(1, queue.PendingCount);

        // Act
        var flushed = await queue.FlushAsync(CancellationToken.None);

        // Assert
        Assert.Equal(0, flushed);
        Assert.Equal(1, queue.PendingCount);
    }

    [Fact]
    public async Task AppendAsync_AfterSuccessfulWrite_FlushesQueuedEntries()
    {
        // Arrange — use a store that fails then recovers
        var recoveringStore = new RecoveringAuditStore(_innerStore);
        using var queue = new DeferredAuditQueue(recoveringStore, _logger, TimeSpan.FromMinutes(10));

        recoveringStore.ShouldFail = true;

        var queued = AuditEntry.Create(
            "TX-AUTO-FLUSH-001", "system", "Created", null, "Created", "corr-af-001", null);
        await queue.AppendAsync(queued, CancellationToken.None);
        Assert.Equal(1, queue.PendingCount);

        // Act — recover and append a new entry (triggers flush of queued)
        recoveringStore.ShouldFail = false;
        var newEntry = AuditEntry.Create(
            "TX-AUTO-FLUSH-001", "system", "Authorized", "Created", "Authorized", "corr-af-001", null);
        await queue.AppendAsync(newEntry, CancellationToken.None);

        // Allow async flush to complete
        await Task.Delay(100);

        // Assert
        Assert.Equal(0, queue.PendingCount);
    }

    /// <summary>
    /// An IAuditStore implementation that always throws to simulate unavailability.
    /// </summary>
    private class FailingAuditStore : Application.Ports.IAuditStore
    {
        public Task AppendAsync(AuditEntry entry, CancellationToken ct)
        {
            throw new InvalidOperationException("Store unavailable");
        }

        public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
        {
            throw new InvalidOperationException("Store unavailable");
        }
    }

    /// <summary>
    /// An IAuditStore that can toggle between failing and delegating to a real store.
    /// </summary>
    private class RecoveringAuditStore : Application.Ports.IAuditStore
    {
        private readonly Application.Ports.IAuditStore _inner;
        public volatile bool ShouldFail;

        public RecoveringAuditStore(Application.Ports.IAuditStore inner)
        {
            _inner = inner;
        }

        public Task AppendAsync(AuditEntry entry, CancellationToken ct)
        {
            if (ShouldFail)
                throw new InvalidOperationException("Store unavailable");
            return _inner.AppendAsync(entry, ct);
        }

        public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct)
        {
            return _inner.GetByTransactionReferenceAsync(transactionReference, ct);
        }
    }
}
