using CardManagement.Application.PlatformServices.Reconciliation.DTOs;

namespace CardManagement.Application.PlatformServices.Reconciliation.Ports;

/// <summary>
/// Worker pool for concurrent file parsing tasks using bounded channels.
/// </summary>
public interface IReconciliationWorkerPool
{
    Task EnqueueFileAsync(FileParseTask task, CancellationToken ct);
    int ActiveWorkerCount { get; }
    int PendingTaskCount { get; }
}
