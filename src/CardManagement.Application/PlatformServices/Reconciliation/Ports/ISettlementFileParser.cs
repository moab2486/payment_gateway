using CardManagement.Application.PlatformServices.Reconciliation.DTOs;
using CardManagement.Domain.PlatformServices.Reconciliation;

namespace CardManagement.Application.PlatformServices.Reconciliation.Ports;

/// <summary>
/// Parses external settlement files from various processors into a standardized internal format.
/// </summary>
public interface ISettlementFileParser
{
    Task<ParseResult> ParseAsync(Stream fileStream, ProcessorType processor, CancellationToken ct);
}
