using CardManagement.Application.PlatformServices.Reconciliation.Commands;
using CardManagement.Application.PlatformServices.Reconciliation.DTOs;
using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Domain.PlatformServices.Reconciliation;

namespace CardManagement.Application.PlatformServices.Reconciliation.Handlers;

/// <summary>
/// Handles the import of a settlement file by checking for duplicates via file hash,
/// creating a batch record, and enqueuing the file to the worker pool for parsing.
/// </summary>
public class ImportSettlementFileCommandHandler
{
    private readonly IReconciliationRepository _repository;
    private readonly IReconciliationWorkerPool _workerPool;

    public ImportSettlementFileCommandHandler(
        IReconciliationRepository repository,
        IReconciliationWorkerPool workerPool)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _workerPool = workerPool ?? throw new ArgumentNullException(nameof(workerPool));
    }

    public async Task<ImportSettlementFileResult> HandleAsync(
        ImportSettlementFileCommand command,
        CancellationToken ct)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        if (string.IsNullOrWhiteSpace(command.FileHash))
            throw new ArgumentException("File hash is required.", nameof(command));

        if (string.IsNullOrWhiteSpace(command.CreatedBy))
            throw new ArgumentException("CreatedBy identity is required.", nameof(command));

        // Duplicate check via file hash
        var isDuplicate = await _repository.FileHashExistsAsync(command.FileHash, ct);
        if (isDuplicate)
        {
            return new ImportSettlementFileResult(
                BatchId: Guid.Empty,
                Accepted: false,
                RejectionReason: "A settlement file with this hash has already been imported.");
        }

        // Create batch in Pending status
        var batch = ReconciliationBatch.Create(
            processor: command.Processor,
            settlementDate: command.SettlementDate,
            fileHash: command.FileHash,
            totalRows: command.TotalRows,
            createdBy: command.CreatedBy);

        await _repository.CreateBatchAsync(batch, ct);

        // Enqueue to worker pool for async processing
        var parseTask = new FileParseTask(
            BatchId: batch.Id,
            FileStream: command.FileStream,
            Processor: command.Processor,
            SettlementDate: command.SettlementDate,
            FileHash: command.FileHash,
            CreatedBy: command.CreatedBy);

        await _workerPool.EnqueueFileAsync(parseTask, ct);

        return new ImportSettlementFileResult(
            BatchId: batch.Id,
            Accepted: true);
    }
}
