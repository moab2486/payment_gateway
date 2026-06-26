using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Application.PlatformServices.Reconciliation.Queries;
using CardManagement.Domain.PlatformServices.Reconciliation;

namespace CardManagement.Application.PlatformServices.Reconciliation.Handlers;

/// <summary>
/// Handles listing reconciliation adjustments with optional filtering by exception ID.
/// </summary>
public class ListAdjustmentsQueryHandler
{
    private readonly IReconciliationRepository _repository;

    public ListAdjustmentsQueryHandler(IReconciliationRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<IReadOnlyList<Adjustment>> HandleAsync(
        ListAdjustmentsQuery query,
        CancellationToken ct)
    {
        if (query is null)
            throw new ArgumentNullException(nameof(query));

        var limit = query.Limit > 0 ? query.Limit : 20;
        var offset = query.Offset >= 0 ? query.Offset : 0;

        return await _repository.ListAdjustmentsAsync(query.ExceptionId, limit, offset, ct);
    }
}
