using CardManagement.Domain.PlatformServices.Reconciliation;

namespace CardManagement.Application.PlatformServices.Reconciliation.Ports;

/// <summary>
/// Repository port for reconciliation aggregate persistence.
/// </summary>
public interface IReconciliationRepository
{
    Task<ReconciliationBatch> CreateBatchAsync(ReconciliationBatch batch, CancellationToken ct);
    Task AddSettlementLineItemsAsync(IEnumerable<SettlementLineItem> items, CancellationToken ct);
    Task AddExceptionsAsync(IEnumerable<ReconciliationException> exceptions, CancellationToken ct);
    Task<ReconciliationBatch?> GetBatchAsync(Guid batchId, CancellationToken ct);
    Task<bool> FileHashExistsAsync(string fileHash, CancellationToken ct);
    Task UpdateBatchAsync(ReconciliationBatch batch, CancellationToken ct);
    Task<IReadOnlyList<ReconciliationBatch>> ListBatchesAsync(int limit, int offset, CancellationToken ct);
    Task<IReadOnlyList<ReconciliationException>> ListExceptionsByBatchAsync(Guid batchId, int limit, int offset, CancellationToken ct);
    Task<ReconciliationException?> GetExceptionAsync(Guid exceptionId, CancellationToken ct);
    Task AddAdjustmentAsync(Adjustment adjustment, CancellationToken ct);
    Task UpdateExceptionAsync(ReconciliationException exception, CancellationToken ct);
    Task<IReadOnlyList<Adjustment>> ListAdjustmentsAsync(Guid? exceptionId, int limit, int offset, CancellationToken ct);
}
