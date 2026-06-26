using CardManagement.Domain.PlatformServices.Reconciliation;

namespace CardManagement.Application.PlatformServices.Reconciliation.DTOs;

/// <summary>
/// Represents a file parsing task to be enqueued to the reconciliation worker pool.
/// </summary>
public record FileParseTask(
    Guid BatchId,
    Stream FileStream,
    ProcessorType Processor,
    DateOnly SettlementDate,
    string FileHash,
    string CreatedBy);
