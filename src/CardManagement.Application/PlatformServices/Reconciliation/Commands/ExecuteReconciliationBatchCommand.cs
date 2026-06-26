namespace CardManagement.Application.PlatformServices.Reconciliation.Commands;

/// <summary>
/// Command to execute reconciliation matching for a batch.
/// </summary>
public record ExecuteReconciliationBatchCommand(Guid BatchId);
