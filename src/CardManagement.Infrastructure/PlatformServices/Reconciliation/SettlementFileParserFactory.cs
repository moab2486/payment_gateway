using CardManagement.Application.PlatformServices.Reconciliation.DTOs;
using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Domain.PlatformServices.Reconciliation;

namespace CardManagement.Infrastructure.PlatformServices.Reconciliation;

/// <summary>
/// Implements ISettlementFileParser by dispatching to the appropriate processor-specific parser
/// based on the ProcessorType. Handles malformed rows gracefully — individual row parse failures
/// are recorded as errors while valid rows continue processing.
/// </summary>
public class SettlementFileParserFactory : ISettlementFileParser
{
    private readonly NibssSettlementFileParser _nibssParser;
    private readonly InterswitchSettlementFileParser _interswitchParser;
    private readonly CardifySettlementFileParser _cardifyParser;

    public SettlementFileParserFactory()
    {
        _nibssParser = new NibssSettlementFileParser();
        _interswitchParser = new InterswitchSettlementFileParser();
        _cardifyParser = new CardifySettlementFileParser();
    }

    public Task<ParseResult> ParseAsync(Stream fileStream, ProcessorType processor, CancellationToken ct)
    {
        return processor switch
        {
            ProcessorType.NIBSS => _nibssParser.ParseAsync(fileStream, ct),
            ProcessorType.Interswitch => _interswitchParser.ParseAsync(fileStream, ct),
            ProcessorType.Cardify => _cardifyParser.ParseAsync(fileStream, ct),
            _ => throw new ArgumentException($"Unsupported processor type: {processor}", nameof(processor))
        };
    }
}
