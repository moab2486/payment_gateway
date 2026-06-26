using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Application.PlatformServices.Reconciliation.Queries;
using CardManagement.Domain.PlatformServices.Reconciliation;

namespace CardManagement.Application.PlatformServices.Reconciliation.Handlers;

/// <summary>
/// Handles retrieval of a single reconciliation batch by ID.
/// </summary>
public class GetBatchQueryHandler
{
    private readonly IReconciliationRepository _repository;

    public GetBatchQueryHandler(IReconciliationRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<ReconciliationBatch?> HandleAsync(GetBatchQuery query, CancellationToken ct)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        if (query.BatchId == Guid.Empty)
            throw new ArgumentException("Batch ID is required.", nameof(query));

        return await _repository.GetBatchAsync(query.BatchId, ct);
    }
}
