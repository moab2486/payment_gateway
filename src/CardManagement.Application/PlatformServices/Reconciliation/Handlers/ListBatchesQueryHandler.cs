using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Application.PlatformServices.Reconciliation.Queries;
using CardManagement.Domain.PlatformServices.Reconciliation;

namespace CardManagement.Application.PlatformServices.Reconciliation.Handlers;

/// <summary>
/// Handles listing reconciliation batches with pagination.
/// </summary>
public class ListBatchesQueryHandler
{
    private readonly IReconciliationRepository _repository;

    public ListBatchesQueryHandler(IReconciliationRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<ReconciliationBatch>> HandleAsync(
        ListBatchesQuery query,
        CancellationToken ct)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        var limit = query.Limit > 0 ? query.Limit : 20;
        var offset = query.Offset >= 0 ? query.Offset : 0;

        return await _repository.ListBatchesAsync(limit, offset, ct);
    }
}
