using CardManagement.Domain.ValueObjects;

namespace CardManagement.Domain.PlatformServices.Reconciliation;

/// <summary>
/// Represents a unit of work for processing one settlement file import and reconciliation run.
/// Tracks parsing progress and overall batch status through its lifecycle.
/// </summary>
public class ReconciliationBatch
{
    public Guid Id { get; private set; }
    public ProcessorType Processor { get; private set; }
    public DateOnly SettlementDate { get; private set; }
    public string FileHash { get; private set; } = string.Empty;
    public BatchStatus Status { get; private set; }
    public int TotalRows { get; private set; }
    public int ParsedRows { get; private set; }
    public int ErrorRows { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    private ReconciliationBatch() { }

    /// <summary>
    /// Creates a new reconciliation batch in Pending status.
    /// </summary>
    public static ReconciliationBatch Create(
        ProcessorType processor,
        DateOnly settlementDate,
        string fileHash,
        int totalRows,
        string createdBy)
    {
        if (string.IsNullOrWhiteSpace(fileHash))
            throw new ArgumentException("File hash is required.", nameof(fileHash));

        if (totalRows < 0)
            throw new ArgumentException("Total rows cannot be negative.", nameof(totalRows));

        if (string.IsNullOrWhiteSpace(createdBy))
            throw new ArgumentException("CreatedBy identity is required.", nameof(createdBy));

        return new ReconciliationBatch
        {
            Id = Guid.NewGuid(),
            Processor = processor,
            SettlementDate = settlementDate,
            FileHash = fileHash,
            Status = BatchStatus.Pending,
            TotalRows = totalRows,
            ParsedRows = 0,
            ErrorRows = 0,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedBy = createdBy
        };
    }

    /// <summary>
    /// Transitions the batch to Processing status.
    /// </summary>
    public void StartProcessing()
    {
        if (Status != BatchStatus.Pending)
            throw new InvalidOperationException(
                $"Cannot start processing from status '{Status}'. Batch must be in Pending status.");

        Status = BatchStatus.Processing;
    }

    /// <summary>
    /// Records parse progress for the batch.
    /// </summary>
    public void RecordParseProgress(int parsedRows, int errorRows)
    {
        if (Status != BatchStatus.Processing)
            throw new InvalidOperationException(
                $"Cannot record parse progress in status '{Status}'. Batch must be in Processing status.");

        if (parsedRows < 0)
            throw new ArgumentException("Parsed rows cannot be negative.", nameof(parsedRows));

        if (errorRows < 0)
            throw new ArgumentException("Error rows cannot be negative.", nameof(errorRows));

        if (parsedRows + errorRows > TotalRows)
            throw new ArgumentException(
                "Sum of parsed rows and error rows cannot exceed total rows.");

        ParsedRows = parsedRows;
        ErrorRows = errorRows;
    }

    /// <summary>
    /// Marks the batch as completed successfully.
    /// </summary>
    public void MarkCompleted()
    {
        if (Status != BatchStatus.Processing)
            throw new InvalidOperationException(
                $"Cannot complete from status '{Status}'. Batch must be in Processing status.");

        Status = BatchStatus.Completed;
        CompletedAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the batch as failed.
    /// </summary>
    public void MarkFailed()
    {
        if (Status is BatchStatus.Completed or BatchStatus.Failed)
            throw new InvalidOperationException(
                $"Cannot fail from terminal status '{Status}'.");

        Status = BatchStatus.Failed;
        CompletedAtUtc = DateTime.UtcNow;
    }
}
