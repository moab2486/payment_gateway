using CardManagement.Application.PlatformServices.Reconciliation.DTOs;

namespace CardManagement.Application.PlatformServices.Reconciliation.Ports;

/// <summary>
/// Event publisher for reconciliation domain events to Kafka.
/// Publishes to platform.reconciliation.* topics.
/// </summary>
public interface IReconciliationEventPublisher
{
    /// <summary>
    /// Publishes an adjustment-created event for downstream consumption by ledger and reporting.
    /// Topic: platform.reconciliation.adjustment-created
    /// </summary>
    Task PublishAdjustmentCreatedAsync(AdjustmentCreatedEvent @event, CancellationToken ct);

    /// <summary>
    /// Publishes a batch-completed event for downstream consumption by admin console.
    /// Topic: platform.reconciliation.batch-completed
    /// </summary>
    Task PublishBatchCompletedAsync(Guid batchId, CancellationToken ct);
}
