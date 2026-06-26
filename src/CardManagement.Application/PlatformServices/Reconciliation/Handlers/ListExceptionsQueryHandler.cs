using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Application.PlatformServices.Reconciliation.Queries;
using CardManagement.Domain.PlatformServices.Reconciliation;

namespace CardManagement.Application.PlatformServices.Reconciliation.Handlers;

/// <summary>
/// Handles listing reconciliation exceptions for a given batch with pagination.
/// </summary>
public class ListExceptionsQueryHandler
{
    private readonly IReconciliationRepository _repository;

    public ListExceptionsQueryHandler(IReconciliationRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<ReconciliationException>> HandleAsync(
        ListExceptionsQuery query,
        CancellationToken ct)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        if (query.BatchId == Guid.Empty)
            throw new ArgumentException("Batch ID is required.", nameof(query));

        var limit = query.Limit > 0 ? query.Limit : 20;
        var offset = query.Offset >= 0 ? query.Offset : 0;

        return await _repository.ListExceptionsByBatchAsync(query.BatchId, limit, offset, ct);
    }
}
