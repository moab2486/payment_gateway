using System.Text.Json;
using CardManagement.Application.PlatformServices.Reconciliation.DTOs;
using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.PlatformServices.Reconciliation;

/// <summary>
/// Reconciliation worker pool that wraps the generic <see cref="WorkerPool{TTask}"/>
/// infrastructure to provide concurrent settlement file processing.
/// 
/// Pipeline per task: parse file → run matching engine → audit the result.
/// 
/// Kafka events are published by the <see cref="ReconciliationEngine"/> internally
/// (batch-completed and adjustment-created) via <see cref="IReconciliationEventPublisher"/>.
/// </summary>
public sealed class ReconciliationWorkerPool : IReconciliationWorkerPool
{
    private readonly WorkerPool<FileParseTask> _innerPool;
    private readonly ILogger<ReconciliationWorkerPool> _logger;

    public ReconciliationWorkerPool(
        WorkerPool<FileParseTask> innerPool,
        ILogger<ReconciliationWorkerPool> logger)
    {
        _innerPool = innerPool ?? throw new ArgumentNullException(nameof(innerPool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task EnqueueFileAsync(FileParseTask task, CancellationToken ct)
    {
        _logger.LogInformation(
            "Enqueuing file parse task for batch {BatchId}, processor {Processor}.",
            task.BatchId, task.Processor);

        await _innerPool.EnqueueAsync(task, ct);
    }

    /// <inheritdoc />
    public int ActiveWorkerCount => _innerPool.ActiveWorkerCount;

    /// <inheritdoc />
    public int PendingTaskCount => _innerPool.PendingTaskCount;

    /// <summary>
    /// Creates the handler delegate that processes each <see cref="FileParseTask"/>.
    /// This factory is used during DI registration to wire the full pipeline.
    /// </summary>
    internal static Func<FileParseTask, CancellationToken, Task> CreateHandler(IServiceProvider sp)
    {
        return async (task, ct) =>
        {
            // Create a scope for each task to get fresh scoped services (DbContext, etc.)
            using var scope = sp.CreateScope();
            var scopedProvider = scope.ServiceProvider;

            var parser = scopedProvider.GetRequiredService<ISettlementFileParser>();
            var repository = scopedProvider.GetRequiredService<IReconciliationRepository>();
            var engine = scopedProvider.GetRequiredService<IReconciliationEngine>();
            var auditStore = scopedProvider.GetRequiredService<IAuditStore>();
            var logger = scopedProvider.GetRequiredService<ILoggerFactory>()
                .CreateLogger("ReconciliationWorkerPool.Handler");

            logger.LogInformation(
                "Processing file parse task for batch {BatchId}, processor {Processor}.",
                task.BatchId, task.Processor);

            // Retrieve and transition the batch to Processing
            var batch = await repository.GetBatchAsync(task.BatchId, ct);
            if (batch is null)
            {
                logger.LogError("Batch {BatchId} not found. Skipping task.", task.BatchId);
                return;
            }

            try
            {
                batch.StartProcessing();
                await repository.UpdateBatchAsync(batch, ct);

                // Step 1: Parse the settlement file
                var parseResult = await parser.ParseAsync(task.FileStream, task.Processor, ct);

                // Step 2: Persist parsed line items
                // Note: BatchId is already set via the Create factory method during parsing
                if (parseResult.LineItems.Count > 0)
                {
                    await repository.AddSettlementLineItemsAsync(parseResult.LineItems, ct);
                }

                // Step 3: Record parse progress on the batch
                batch.RecordParseProgress(parseResult.ParsedRows, parseResult.ErrorRows);
                await repository.UpdateBatchAsync(batch, ct);

                // Step 4: Audit the import result
                var auditEntry = AuditEntry.Create(
                    transactionReference: $"reconciliation-batch:{task.BatchId}",
                    actorIdentity: task.CreatedBy,
                    action: "settlement-file-imported",
                    previousState: null,
                    newState: JsonSerializer.Serialize(new
                    {
                        BatchId = task.BatchId,
                        Processor = task.Processor.ToString(),
                        TotalRows = parseResult.TotalRows,
                        ParsedRows = parseResult.ParsedRows,
                        ErrorRows = parseResult.ErrorRows
                    }),
                    correlationId: task.BatchId.ToString(),
                    previousEntryHash: null);

                await auditStore.AppendAsync(auditEntry, ct);

                // Step 5: Run the reconciliation matching engine
                // This internally handles: matching, exception creation, adjustment evaluation,
                // and Kafka event publishing (batch-completed + adjustment-created)
                var result = await engine.ExecuteBatchAsync(task.BatchId, ct);

                if (result.Success)
                {
                    batch.MarkCompleted();
                    await repository.UpdateBatchAsync(batch, ct);

                    logger.LogInformation(
                        "Batch {BatchId} completed successfully. Matched: {Matched}, Exceptions: {Exceptions}, AutoResolved: {AutoResolved}.",
                        task.BatchId, result.MatchedCount, result.ExceptionCount, result.AutoResolvedCount);
                }
                else
                {
                    batch.MarkFailed();
                    await repository.UpdateBatchAsync(batch, ct);

                    logger.LogWarning(
                        "Batch {BatchId} matching failed: {Error}.",
                        task.BatchId, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error processing batch {BatchId}. Marking as failed.", task.BatchId);

                try
                {
                    batch.MarkFailed();
                    await repository.UpdateBatchAsync(batch, ct);
                }
                catch (Exception updateEx)
                {
                    logger.LogError(updateEx,
                        "Failed to mark batch {BatchId} as failed after processing error.",
                        task.BatchId);
                }

                throw; // Let the WorkerPool log the error and continue with next task
            }
            finally
            {
                // Dispose the file stream if it's still open
                if (task.FileStream.CanRead)
                {
                    await task.FileStream.DisposeAsync();
                }
            }
        };
    }
}
