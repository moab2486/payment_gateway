using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ILedgerEntryRepository.
/// Ledger entries are append-only (immutable after creation).
/// </summary>
public class LedgerEntryRepository : ILedgerEntryRepository
{
    private readonly CardManagementDbContext _dbContext;

    public LedgerEntryRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task AddRangeAsync(IEnumerable<LedgerEntry> entries, CancellationToken cancellationToken = default)
    {
        await _dbContext.LedgerEntries.AddRangeAsync(entries, cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LedgerEntry>> GetByTransactionIdAsync(Guid transactionId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.LedgerEntries
            .Where(e => e.TransactionId == transactionId)
            .OrderBy(e => e.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
