using CardManagement.Application.Ports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CardManagement.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of IUnitOfWork backed by the CardManagementDbContext.
/// Manages database transactions for atomic operations and supports PostgreSQL-specific
/// features like SET LOCAL lock_timeout for deadlock prevention.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly CardManagementDbContext _dbContext;
    private IDbContextTransaction? _transaction;
    private bool _disposed;

    public UnitOfWork(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException(
                "A transaction is already in progress. Commit or rollback the current transaction before starting a new one.");
        }

        _transaction = await _dbContext.Database
            .BeginTransactionAsync(ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Executes raw SQL within the current transaction context.
    /// Used for PostgreSQL-specific commands like SET LOCAL lock_timeout = '5000'.
    /// SET LOCAL scopes the setting to the current transaction only.
    /// </remarks>
    public async Task ExecuteSqlAsync(string sql, CancellationToken ct = default)
    {
        await _dbContext.Database
            .ExecuteSqlRawAsync(sql, ct)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
        {
            throw new InvalidOperationException("No transaction in progress to commit.");
        }

        try
        {
            await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
            await _transaction.CommitAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await DisposeTransactionAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
        {
            return; // No transaction to rollback — idempotent behavior
        }

        try
        {
            await _transaction.RollbackAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await DisposeTransactionAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            await DisposeTransactionAsync().ConfigureAwait(false);
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _transaction?.Dispose();
            _transaction = null;
            _disposed = true;
        }
    }

    private async ValueTask DisposeTransactionAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.DisposeAsync().ConfigureAwait(false);
            _transaction = null;
        }
    }
}
