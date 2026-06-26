using CardManagement.Domain.PlatformServices.Reconciliation;

namespace CardManagement.Application.PlatformServices.Reconciliation.Commands;

/// <summary>
/// Command to import a settlement file for reconciliation processing.
/// </summary>
public record ImportSettlementFileCommand(
    Stream FileStream,
    ProcessorType Processor,
    DateOnly SettlementDate,
    string FileHash,
    int TotalRows,
    string CreatedBy);

/// <summary>
/// Result of the import settlement file command.
/// </summary>
public record ImportSettlementFileResult(
    Guid BatchId,
    bool Accepted,
    string? RejectionReason = null);
