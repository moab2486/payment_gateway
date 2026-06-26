using System.Collections.Concurrent;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.Audit;

/// <summary>
/// Decorator around IAuditStore that provides deferred writing capability.
/// When the underlying store is unavailable, entries are buffered in a thread-safe
/// in-memory queue and flushed automatically when connectivity is restored.
/// </summary>
public class DeferredAuditQueue : IAuditStore, IDisposable
{
    private readonly IAuditStore _innerStore;
    private readonly ILogger<DeferredAuditQueue> _logger;
    private readonly ConcurrentQueue<AuditEntry> _pendingEntries = new();
    private readonly Timer _flushTimer;
    private readonly TimeSpan _flushInterval;
    private volatile bool _isFlushing;
    private volatile bool _disposed;

    public DeferredAuditQueue(
        IAuditStore innerStore,
        ILogger<DeferredAuditQueue> logger,
        TimeSpan? flushInterval = null)
    {
        _innerStore = innerStore ?? throw new ArgumentNullException(nameof(innerStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(5);

        _flushTimer = new Timer(
            callback: _ => _ = FlushAsync(CancellationToken.None),
            state: null,
            dueTime: _flushInterval,
            period: _flushInterval);
    }

    /// <summary>
    /// Number of entries currently queued for deferred writing.
    /// </summary>
    public int PendingCount => _pendingEntries.Count;

    /// <inheritdoc />
    public async Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        if (entry is null)
            throw new ArgumentNullException(nameof(entry));

        try
        {
            await _innerStore.AppendAsync(entry, ct);

            // After a successful write, attempt to flush any queued entries
            if (!_pendingEntries.IsEmpty)
            {
                _ = FlushAsync(ct);
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Audit store unavailable. Queuing entry for deferred writing. " +
                "TransactionReference={TransactionReference}, CorrelationId={CorrelationId}",
                entry.TransactionReference, entry.CorrelationId);

            _pendingEntries.Enqueue(entry);
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(
        string transactionReference, CancellationToken ct)
    {
        return _innerStore.GetByTransactionReferenceAsync(transactionReference, ct);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AuditEntry>> QueryAsync(
        string? actorIdentity,
        string? action,
        DateTime? fromUtc,
        DateTime? toUtc,
        int limit,
        int offset,
        CancellationToken ct)
    {
        return _innerStore.QueryAsync(actorIdentity, action, fromUtc, toUtc, limit, offset, ct);
    }

    /// <summary>
    /// Attempts to flush all queued entries to the inner store.
    /// Returns the number of entries successfully flushed.
    /// </summary>
    public async Task<int> FlushAsync(CancellationToken ct)
    {
        if (_isFlushing || _pendingEntries.IsEmpty)
            return 0;

        _isFlushing = true;
        var flushedCount = 0;

        try
        {
            while (_pendingEntries.TryDequeue(out var entry))
            {
                try
                {
                    await _innerStore.AppendAsync(entry, ct);
                    flushedCount++;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    // Re-enqueue the failed entry and stop flushing —
                    // store is still unavailable
                    _pendingEntries.Enqueue(entry);
                    _logger.LogWarning(ex,
                        "Failed to flush deferred audit entry. " +
                        "Remaining queued: {Count}",
                        _pendingEntries.Count);
                    break;
                }
            }

            if (flushedCount > 0)
            {
                _logger.LogInformation(
                    "Successfully flushed {Count} deferred audit entries.", flushedCount);
            }
        }
        finally
        {
            _isFlushing = false;
        }

        return flushedCount;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _flushTimer.Dispose();
    }
}
