using CardManagement.Domain.Entities;

namespace CardManagement.Application.Ports.Repositories;

/// <summary>
/// Repository interface for Account aggregate persistence operations.
/// </summary>
public interface IAccountRepository
{
    /// <summary>
    /// Retrieves an account by its unique identifier.
    /// </summary>
    Task<Account?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves an account by its unique identifier with a PostgreSQL row-level lock (SELECT FOR UPDATE).
    /// This must be called within an active database transaction to serialize concurrent writes
    /// and prevent race conditions during balance updates.
    /// </summary>
    Task<Account?> GetByIdForUpdateAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists updated balance and timestamp for an existing account entity.
    /// Should be called after acquiring a row-level lock via <see cref="GetByIdForUpdateAsync"/>.
    /// </summary>
    Task UpdateBalanceAsync(Account account, CancellationToken cancellationToken = default);
}
