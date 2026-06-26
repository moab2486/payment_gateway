using System.Buffers;
using System.IO.Pipelines;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.Tcp;

/// <summary>
/// Handles inbound TCP connections by reading ISO 8583 message bytes from a PipeReader,
/// parsing them via the ISO 8583 Gateway, routing based on MTI and BIN, dispatching to
/// the appropriate pipeline (network management, authorization, or reversal), and writing
/// the response bytes back to the PipeWriter.
/// 
/// Wiring: TcpListenerService → TcpConnectionHandler → Iso8583GatewayAdapter → CardProcessorRouter → TransactionPipeline
/// </summary>
public sealed class TcpConnectionHandler : ITcpConnectionHandler
{
    private readonly IIso8583Gateway _iso8583Gateway;
    private readonly ICardProcessorRouter _cardProcessorRouter;
    private readonly ITransactionPipeline _transactionPipeline;
    private readonly INetworkManagementHandler _networkManagementHandler;
    private readonly ILogger<TcpConnectionHandler> _logger;

    // MTI constants
    private const string MtiNetworkManagement = "0800";
    private const string MtiAuthorization = "0100";
    private const string MtiReversal = "0420";

    // ISO 8583 field numbers
    private const int FieldPan = 2;
    private const int FieldForwardingInstitution = 33;
    private const int FieldResponseCode = 39;

    // Response codes
    private const string ResponseCodeSystemMalfunction = "96";
    private const string ResponseCodeSignOnRequired = "06";

    public TcpConnectionHandler(
        IIso8583Gateway iso8583Gateway,
        ICardProcessorRouter cardProcessorRouter,
        ITransactionPipeline transactionPipeline,
        INetworkManagementHandler networkManagementHandler,
        ILogger<TcpConnectionHandler> logger)
    {
        _iso8583Gateway = iso8583Gateway ?? throw new ArgumentNullException(nameof(iso8583Gateway));
        _cardProcessorRouter = cardProcessorRouter ?? throw new ArgumentNullException(nameof(cardProcessorRouter));
        _transactionPipeline = transactionPipeline ?? throw new ArgumentNullException(nameof(transactionPipeline));
        _networkManagementHandler = networkManagementHandler ?? throw new ArgumentNullException(nameof(networkManagementHandler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task HandleConnectionAsync(PipeReader reader, PipeWriter writer, CancellationToken ct)
    {
        // Step 1: Read the message bytes from PipeReader
        var readResult = await reader.ReadAsync(ct).ConfigureAwait(false);
        var buffer = readResult.Buffer;

        try
        {
            if (buffer.Length == 0)
            {
                reader.AdvanceTo(buffer.End);
                return;
            }

            // Convert the buffer to a byte array for parsing
            byte[] messageBytes;
            if (buffer.IsSingleSegment)
            {
                messageBytes = buffer.FirstSpan.ToArray();
            }
            else
            {
                messageBytes = new byte[buffer.Length];
                buffer.CopyTo(messageBytes);
            }

            reader.AdvanceTo(buffer.End);

            // Step 2: Parse the raw bytes into an Iso8583Message
            var parseResult = _iso8583Gateway.Parse(messageBytes);

            if (!parseResult.IsSuccess)
            {
                _logger.LogWarning(
                    "Failed to parse ISO 8583 message: {Error}",
                    parseResult.ErrorMessage);

                // Construct an error response and write it back
                var errorResponse = ConstructErrorResponse(
                    parseResult.ErrorCode ?? ResponseCodeSystemMalfunction);
                await WriteResponseAsync(writer, errorResponse, ct).ConfigureAwait(false);
                return;
            }

            var message = parseResult.Value!;

            // Step 3: Determine message type and route accordingly
            Iso8583Message? response = message.Mti switch
            {
                MtiNetworkManagement => await HandleNetworkManagementAsync(message, ct).ConfigureAwait(false),
                MtiAuthorization => await HandleFinancialMessageAsync(message, isReversal: false, ct).ConfigureAwait(false),
                MtiReversal => await HandleFinancialMessageAsync(message, isReversal: true, ct).ConfigureAwait(false),
                _ => HandleUnsupportedMti(message.Mti)
            };

            // Step 8: Write the response bytes to PipeWriter
            if (response != null)
            {
                await WriteResponseAsync(writer, response, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error processing ISO 8583 message in connection handler");
            try
            {
                var errorResponse = ConstructErrorResponse(ResponseCodeSystemMalfunction);
                await WriteResponseAsync(writer, errorResponse, ct).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort error response; swallow if write fails
            }
        }
    }

    /// <summary>
    /// Handles network management messages (MTI 0800) by delegating to INetworkManagementHandler.
    /// </summary>
    private async Task<Iso8583Message?> HandleNetworkManagementAsync(
        Iso8583Message message, CancellationToken ct)
    {
        _logger.LogDebug("Processing network management message (MTI 0800)");

        // Determine processor from message context
        var processor = DetermineProcessorForNetworkMessage(message);

        var result = await _networkManagementHandler.HandleNetworkManagementAsync(
            message, processor, ct).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "Network management handling failed: {Error}",
                result.ErrorMessage);
            return ConstructErrorResponse(result.ErrorCode ?? ResponseCodeSystemMalfunction);
        }

        return result.Value!;
    }

    /// <summary>
    /// Handles financial messages (authorization 0100, reversal 0420) by:
    /// 1. Resolving the processor from the PAN via BIN routing
    /// 2. Checking the sign-on gate
    /// 3. Dispatching to the appropriate pipeline
    /// </summary>
    private async Task<Iso8583Message?> HandleFinancialMessageAsync(
        Iso8583Message message, bool isReversal, CancellationToken ct)
    {
        var mtiLabel = isReversal ? "reversal" : "authorization";

        // Extract PAN for BIN-based routing
        if (!message.Fields.TryGetValue(FieldPan, out var pan) || string.IsNullOrEmpty(pan))
        {
            _logger.LogWarning(
                "Financial message ({MtiLabel}) missing PAN field, cannot route",
                mtiLabel);
            return ConstructErrorResponse(ResponseCodeSystemMalfunction);
        }

        // Route by BIN to determine the processor
        ProcessorType processor;
        try
        {
            processor = _cardProcessorRouter.ResolveProcessor(pan);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                "BIN routing failed for {MtiLabel}: {Error}",
                mtiLabel, ex.Message);
            return ConstructErrorResponse(ResponseCodeSystemMalfunction);
        }

        // Check sign-on gate before processing financial messages
        var gateResult = _networkManagementHandler.ValidateFinancialMessageAllowed(message, processor);
        if (!gateResult.IsSuccess)
        {
            _logger.LogWarning(
                "Financial message ({MtiLabel}) blocked: processor {Processor} not signed on",
                mtiLabel, processor);
            return ConstructSignOnRequiredResponse(message);
        }

        // Dispatch to the appropriate pipeline
        _logger.LogDebug(
            "Dispatching {MtiLabel} to pipeline for processor {Processor}",
            mtiLabel, processor);

        if (isReversal)
        {
            return await _transactionPipeline.ProcessReversalAsync(message, ct).ConfigureAwait(false);
        }
        else
        {
            return await _transactionPipeline.ProcessAuthorizationAsync(message, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Handles an unsupported MTI by logging a warning and returning an error response.
    /// </summary>
    private Iso8583Message? HandleUnsupportedMti(string mti)
    {
        _logger.LogWarning("Unsupported MTI received: {Mti}", mti);
        return ConstructErrorResponse(ResponseCodeSystemMalfunction);
    }

    /// <summary>
    /// Determines the processor type for a network management message.
    /// Network management messages may not have a PAN, so we attempt to resolve
    /// from forwarding institution (field 33) or default to Interswitch.
    /// </summary>
    private ProcessorType DetermineProcessorForNetworkMessage(Iso8583Message message)
    {
        // Try field 33 (forwarding institution ID) as a hint
        if (message.Fields.TryGetValue(FieldForwardingInstitution, out var institutionId)
            && !string.IsNullOrEmpty(institutionId))
        {
            // CardFi institution IDs start with "CF" by convention
            if (institutionId.StartsWith("CF", StringComparison.OrdinalIgnoreCase))
                return ProcessorType.CardFi;
        }

        // Try PAN if present
        if (message.Fields.TryGetValue(FieldPan, out var pan) && !string.IsNullOrEmpty(pan))
        {
            try
            {
                return _cardProcessorRouter.ResolveProcessor(pan);
            }
            catch (InvalidOperationException)
            {
                // Fall through to default
            }
        }

        // Default to Interswitch for network management without routing context
        return ProcessorType.Interswitch;
    }

    /// <summary>
    /// Writes the ISO 8583 response message back to the PipeWriter by constructing
    /// the binary representation and writing bytes to the pipe.
    /// </summary>
    private async Task WriteResponseAsync(PipeWriter writer, Iso8583Message response, CancellationToken ct)
    {
        var constructResult = _iso8583Gateway.Construct(response);

        if (!constructResult.IsSuccess)
        {
            _logger.LogError(
                "Failed to construct response bytes: {Error}",
                constructResult.ErrorMessage);

            // Attempt a minimal fallback error response
            var fallbackResponse = ConstructErrorResponse(ResponseCodeSystemMalfunction);
            var fallbackResult = _iso8583Gateway.Construct(fallbackResponse);

            if (fallbackResult.IsSuccess)
            {
                await WriteBytesToPipeAsync(writer, fallbackResult.Value!, ct).ConfigureAwait(false);
            }
            return;
        }

        await WriteBytesToPipeAsync(writer, constructResult.Value!, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes raw bytes to the PipeWriter and flushes.
    /// </summary>
    private static async Task WriteBytesToPipeAsync(PipeWriter writer, byte[] data, CancellationToken ct)
    {
        var memory = writer.GetMemory(data.Length);
        data.CopyTo(memory);
        writer.Advance(data.Length);
        await writer.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Constructs a generic error response message with the specified response code.
    /// Uses MTI 0810 (network management response) as a generic error carrier.
    /// </summary>
    private static Iso8583Message ConstructErrorResponse(string responseCode)
    {
        return new Iso8583Message
        {
            Mti = "0810",
            Fields = new Dictionary<int, string>
            {
                [FieldResponseCode] = responseCode
            }
        };
    }

    /// <summary>
    /// Constructs a response indicating that the processor has not completed sign-on.
    /// Maps the request MTI to the appropriate response MTI.
    /// </summary>
    private static Iso8583Message ConstructSignOnRequiredResponse(Iso8583Message request)
    {
        var responseMti = request.Mti switch
        {
            MtiAuthorization => "0110",
            MtiReversal => "0430",
            _ => "0810"
        };

        var responseFields = new Dictionary<int, string>
        {
            [FieldResponseCode] = ResponseCodeSignOnRequired
        };

        // Echo PAN if available for correlation
        if (request.Fields.TryGetValue(FieldPan, out var pan))
        {
            responseFields[FieldPan] = pan;
        }

        return new Iso8583Message
        {
            Mti = responseMti,
            Fields = responseFields
        };
    }
}
