namespace CardManagement.Application.Ports;

/// <summary>
/// Abstraction for managing database transactions, enabling atomic operations
/// across multiple repository calls within the Ledger Service.
/// Implementations must support PostgreSQL-specific features like SET LOCAL lock_timeout.
/// </summary>
public interface IUnitOfWork : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Begins a new database transaction.
    /// </summary>
    Task BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>
    /// Executes a raw SQL command within the current transaction context.
    /// Used for PostgreSQL-specific commands like SET LOCAL lock_timeout.
    /// </summary>
    Task ExecuteSqlAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// Commits the current transaction, persisting all changes atomically.
    /// </summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>
    /// Rolls back the current transaction, discarding all uncommitted changes.
    /// </summary>
    Task RollbackAsync(CancellationToken ct = default);
}
