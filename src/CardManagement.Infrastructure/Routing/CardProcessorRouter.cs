using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Application.Ports.Repositories;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Services;
using CardManagement.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace CardManagement.Infrastructure.Routing;

/// <summary>
/// Routes ISO 8583 messages to the correct card processor based on BIN range identification.
/// Loads BIN ranges from configuration, maintains sign-on state per processor,
/// and handles network management messages (sign-on, echo, key exchange).
/// </summary>
public class CardProcessorRouter : ICardProcessorRouter
{
    private readonly IReadOnlyList<BinRange> _binRanges;
    private readonly IProcessorSessionRepository _sessionRepository;
    private readonly Dictionary<ProcessorType, bool> _signOnState;
    private readonly object _stateLock = new();

    public CardProcessorRouter(
        IConfiguration configuration,
        IProcessorSessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
        _binRanges = LoadBinRanges(configuration);
        _signOnState = new Dictionary<ProcessorType, bool>
        {
            [ProcessorType.Interswitch] = false,
            [ProcessorType.CardFi] = false
        };
    }

    /// <inheritdoc />
    public ProcessorType ResolveProcessor(string pan)
    {
        if (string.IsNullOrEmpty(pan))
            throw new InvalidOperationException("PAN does not match any configured BIN range.");

        var binRange = BinRangeResolver.Resolve(pan, _binRanges);

        if (binRange is null)
            throw new InvalidOperationException($"PAN does not match any configured BIN range.");

        return MapSchemeToProcessor(binRange.Scheme);
    }

    /// <inheritdoc />
    public bool IsSignedOn(ProcessorType processor)
    {
        lock (_stateLock)
        {
            return _signOnState.TryGetValue(processor, out var isSignedOn) && isSignedOn;
        }
    }

    /// <inheritdoc />
    public async Task<Result> ProcessNetworkManagement(Iso8583Message message, ProcessorType processor)
    {
        if (message is null)
            return Result.Failure("Message cannot be null.", "INVALID_MESSAGE");

        var mti = message.Mti;

        // Network management requests are MTI 0800
        if (mti != "0800")
            return Result.Failure($"Unsupported network management MTI: {mti}", "UNSUPPORTED_MTI");

        // Determine the function code from field 70 (Network Management Information Code)
        var functionCode = message.Fields.TryGetValue(70, out var code) ? code : null;

        return functionCode switch
        {
            "001" => await HandleSignOn(processor),   // Sign-on
            "301" => await HandleEchoTest(processor), // Echo test
            "161" => await HandleKeyExchange(processor), // Key exchange
            _ => await HandleSignOn(processor) // Default to sign-on if no function code
        };
    }

    private async Task<Result> HandleSignOn(ProcessorType processor)
    {
        lock (_stateLock)
        {
            _signOnState[processor] = true;
        }

        // Update persistent session state
        var session = await _sessionRepository.GetByTypeAsync(processor);
        if (session != null)
        {
            session.SignOn();
            await _sessionRepository.UpdateAsync(session);
        }

        return Result.Success();
    }

    private async Task<Result> HandleEchoTest(ProcessorType processor)
    {
        // Update heartbeat timestamp
        var session = await _sessionRepository.GetByTypeAsync(processor);
        if (session != null)
        {
            session.RecordHeartbeat();
            await _sessionRepository.UpdateAsync(session);
        }

        return Result.Success();
    }

    private Task<Result> HandleKeyExchange(ProcessorType processor)
    {
        // Key exchange acknowledgment — no state change required beyond success response
        return Task.FromResult(Result.Success());
    }

    private static ProcessorType MapSchemeToProcessor(CardScheme scheme)
    {
        return scheme switch
        {
            CardScheme.Verve => ProcessorType.Interswitch,
            CardScheme.Visa => ProcessorType.CardFi,
            CardScheme.Mastercard => ProcessorType.CardFi,
            _ => throw new InvalidOperationException($"Unknown card scheme: {scheme}")
        };
    }

    private static IReadOnlyList<BinRange> LoadBinRanges(IConfiguration configuration)
    {
        var binRangesJson = configuration["CARD__BIN_RANGES"];

        if (string.IsNullOrWhiteSpace(binRangesJson))
            return Array.Empty<BinRange>();

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        var dtos = JsonSerializer.Deserialize<List<BinRangeDto>>(binRangesJson, options);

        if (dtos is null || dtos.Count == 0)
            return Array.Empty<BinRange>();

        var ranges = new List<BinRange>();
        foreach (var dto in dtos)
        {
            var scheme = Enum.Parse<CardScheme>(dto.Scheme, ignoreCase: true);
            ranges.Add(new BinRange(dto.Prefix, scheme, dto.PanLength));
        }

        return ranges.AsReadOnly();
    }

    private sealed class BinRangeDto
    {
        public string Prefix { get; set; } = string.Empty;
        public string Scheme { get; set; } = string.Empty;
        public int PanLength { get; set; }
    }
}
