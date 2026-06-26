using CardManagement.Application.PlatformServices.Reconciliation.DTOs;

namespace CardManagement.Application.PlatformServices.Reconciliation.Ports;

/// <summary>
/// Executes the reconciliation matching process for a batch, comparing settlement line items
/// against internal PaymentRequest records to identify exceptions.
/// </summary>
public interface IReconciliationEngine
{
    Task<ReconciliationBatchResult> ExecuteBatchAsync(Guid batchId, CancellationToken ct);
}
