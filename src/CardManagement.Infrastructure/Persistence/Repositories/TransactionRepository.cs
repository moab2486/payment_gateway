using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ITransactionRepository.
/// Provides persistence operations for TransactionRecord entities.
/// </summary>
public class TransactionRepository : ITransactionRepository
{
    private readonly CardManagementDbContext _dbContext;

    public TransactionRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task AddAsync(TransactionRecord transaction, CancellationToken cancellationToken = default)
    {
        await _dbContext.TransactionRecords.AddAsync(transaction, cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<TransactionRecord?> GetByStanAsync(string systemTraceAuditNumber, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TransactionRecords
            .FirstOrDefaultAsync(t => t.SystemTraceAuditNumber == systemTraceAuditNumber, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(TransactionRecord transaction, CancellationToken cancellationToken = default)
    {
        _dbContext.TransactionRecords.Update(transaction);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
