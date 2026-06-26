using CardManagement.Domain.Entities;

namespace CardManagement.Infrastructure.Saga;

/// <summary>
/// Repository interface for persisting and retrieving saga state.
/// </summary>
public interface ISagaStateRepository
{
    /// <summary>
    /// Persists a new saga state to the database.
    /// </summary>
    Task CreateAsync(SagaState state, CancellationToken ct);

    /// <summary>
    /// Updates an existing saga state in the database.
    /// </summary>
    Task UpdateAsync(SagaState state, CancellationToken ct);

    /// <summary>
    /// Retrieves a saga state by its unique identifier.
    /// </summary>
    Task<SagaState?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Retrieves a saga state by the transaction reference.
    /// </summary>
    Task<SagaState?> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct);
}
