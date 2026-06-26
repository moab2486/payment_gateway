using CardManagement.Domain.Entities;

namespace CardManagement.Application.Ports.Repositories;

/// <summary>
/// Repository for persisting and retrieving DisputeRecord entities.
/// </summary>
public interface IDisputeRepository
{
    Task SaveAsync(DisputeRecord dispute, CancellationToken ct);
    Task UpdateAsync(DisputeRecord dispute, CancellationToken ct);
    Task<DisputeRecord?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<DisputeRecord?> GetByTransactionReferenceAsync(string transactionReference, CancellationToken ct);
}
