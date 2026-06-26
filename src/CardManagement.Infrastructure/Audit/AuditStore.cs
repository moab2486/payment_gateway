using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.Audit;

/// <summary>
/// PostgreSQL-backed append-only audit store with SHA-256 hash chain integrity.
/// Entries are never modified or deleted — the store only supports appending new entries
/// and querying by transaction reference.
/// </summary>
public class AuditStore : IAuditStore
{
    private readonly CardManagementDbContext _dbContext;

    public AuditStore(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task AppendAsync(AuditEntry entry, CancellationToken ct)
    {
        if (entry is null)
            throw new ArgumentNullException(nameof(entry));

        // Retrieve the last entry's hash for this transaction reference to build the chain.
        // If this is the first entry for the transaction, previousHash will be null.
        var lastEntryHash = await _dbContext.AuditEntries
            .Where(e => e.TransactionReference == entry.TransactionReference)
            .OrderByDescending(e => e.Id)
            .Select(e => e.EntryHash)
            .FirstOrDefaultAsync(ct);

        // If the entry's PreviousEntryHash doesn't match the stored chain,
        // recompute the entry with the correct chain link.
        // This ensures hash chain integrity even if the caller didn't know the latest hash.
        if (entry.PreviousEntryHash != lastEntryHash)
        {
            entry = AuditEntry.Create(
                entry.TransactionReference,
                entry.ActorIdentity,
                entry.Action,
                entry.PreviousState,
                entry.NewState,
                entry.CorrelationId,
                lastEntryHash);
        }

        _dbContext.AuditEntries.Add(entry);
        await _dbContext.SaveChangesAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditEntry>> GetByTransactionReferenceAsync(
        string transactionReference, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(transactionReference))
            throw new ArgumentException("Transaction reference is required.", nameof(transactionReference));

        return await _dbContext.AuditEntries
            .AsNoTracking()
            .Where(e => e.TransactionReference == transactionReference)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AuditEntry>> QueryAsync(
        string? actorIdentity,
        string? action,
        DateTime? fromUtc,
        DateTime? toUtc,
        int limit,
        int offset,
        CancellationToken ct)
    {
        var query = _dbContext.AuditEntries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(actorIdentity))
            query = query.Where(e => e.ActorIdentity == actorIdentity);

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(e => e.Action == action);

        if (fromUtc.HasValue)
            query = query.Where(e => e.TimestampUtc >= fromUtc.Value);

        if (toUtc.HasValue)
            query = query.Where(e => e.TimestampUtc <= toUtc.Value);

        return await query
            .OrderByDescending(e => e.TimestampUtc)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }
}
