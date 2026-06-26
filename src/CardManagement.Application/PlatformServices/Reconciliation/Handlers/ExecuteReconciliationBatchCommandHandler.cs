using CardManagement.Application.PlatformServices.Reconciliation.Commands;
using CardManagement.Application.PlatformServices.Reconciliation.DTOs;
using CardManagement.Application.PlatformServices.Reconciliation.Ports;

namespace CardManagement.Application.PlatformServices.Reconciliation.Handlers;

/// <summary>
/// Handles execution of reconciliation matching for a batch by orchestrating the matching engine.
/// Transitions the batch through its lifecycle states (Pending → Processing → Completed/Failed).
/// </summary>
public class ExecuteReconciliationBatchCommandHandler
{
    private readonly IReconciliationRepository _repository;
    private readonly IReconciliationEngine _reconciliationEngine;

    public ExecuteReconciliationBatchCommandHandler(
        IReconciliationRepository repository,
        IReconciliationEngine reconciliationEngine)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _reconciliationEngine = reconciliationEngine ?? throw new ArgumentNullException(nameof(reconciliationEngine));
    }

    public async Task<ReconciliationBatchResult> HandleAsync(
        ExecuteReconciliationBatchCommand command,
        CancellationToken ct)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        if (command.BatchId == Guid.Empty)
            throw new ArgumentException("Batch ID is required.", nameof(command));

        var batch = await _repository.GetBatchAsync(command.BatchId, ct);
        if (batch is null)
        {
            return new ReconciliationBatchResult
            {
                BatchId = command.BatchId,
                TotalItems = 0,
                MatchedCount = 0,
                ExceptionCount = 0,
                AutoResolvedCount = 0,
                Success = false,
                ErrorMessage = $"Batch '{command.BatchId}' not found."
            };
        }

        // Transition batch to Processing status
        batch.StartProcessing();
        await _repository.UpdateBatchAsync(batch, ct);

        try
        {
            // Delegate to the reconciliation engine for actual matching
            var result = await _reconciliationEngine.ExecuteBatchAsync(command.BatchId, ct);

            // Mark batch as completed
            batch.MarkCompleted();
            await _repository.UpdateBatchAsync(batch, ct);

            return result;
        }
        catch (Exception ex)
        {
            // Mark batch as failed on unhandled error
            batch.MarkFailed();
            await _repository.UpdateBatchAsync(batch, ct);

            return new ReconciliationBatchResult
            {
                BatchId = command.BatchId,
                TotalItems = 0,
                MatchedCount = 0,
                ExceptionCount = 0,
                AutoResolvedCount = 0,
                Success = false,
                ErrorMessage = $"Batch execution failed: {ex.Message}"
            };
        }
    }
}
