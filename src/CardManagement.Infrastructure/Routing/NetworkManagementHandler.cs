using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace CardManagement.Infrastructure.Routing;

/// <summary>
/// Handles ISO 8583 network management messages (0800/0810) including sign-on, echo test,
/// and key exchange. Constructs proper response messages with response code "00" (approved),
/// delegates state management to the CardProcessorRouter, and blocks financial messages
/// until a successful sign-on exchange has been completed.
/// </summary>
public class NetworkManagementHandler : INetworkManagementHandler
{
    private readonly ICardProcessorRouter _router;
    private readonly ILogger<NetworkManagementHandler> _logger;

    // ISO 8583 Field numbers
    private const int FieldResponseCode = 39;
    private const int FieldNetworkManagementCode = 70;

    // MTI values
    private const string MtiNetworkRequest = "0800";
    private const string MtiNetworkResponse = "0810";

    // Function codes (field 70)
    private const string FunctionCodeSignOn = "001";
    private const string FunctionCodeEchoTest = "301";
    private const string FunctionCodeKeyExchange = "161";

    // Response codes
    private const string ResponseCodeApproved = "00";

    // Financial MTIs
    private static readonly HashSet<string> FinancialMtis = new(StringComparer.Ordinal)
    {
        "0100", // Authorization request
        "0110", // Authorization response
        "0420", // Reversal request
        "0430"  // Reversal response
    };

    public NetworkManagementHandler(
        ICardProcessorRouter router,
        ILogger<NetworkManagementHandler> logger)
    {
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<Iso8583Message>> HandleNetworkManagementAsync(
        Iso8583Message request, ProcessorType processor, CancellationToken ct = default)
    {
        if (request is null)
            return Result<Iso8583Message>.Failure("Network management request cannot be null.", "INVALID_MESSAGE");

        if (request.Mti != MtiNetworkRequest)
            return Result<Iso8583Message>.Failure(
                $"Expected MTI {MtiNetworkRequest} for network management but received {request.Mti}.",
                "UNSUPPORTED_MTI");

        // Delegate state management to the router
        var routerResult = await _router.ProcessNetworkManagement(request, processor);
        if (!routerResult.IsSuccess)
        {
            _logger.LogWarning(
                "Network management processing failed for processor {Processor}: {Error}",
                processor, routerResult.ErrorMessage);
            return Result<Iso8583Message>.Failure(routerResult.ErrorMessage!, routerResult.ErrorCode);
        }

        // Determine function code for logging
        var functionCode = request.Fields.TryGetValue(FieldNetworkManagementCode, out var code)
            ? code
            : "unknown";

        _logger.LogInformation(
            "Network management {FunctionCode} processed successfully for processor {Processor}",
            functionCode, processor);

        // Construct the 0810 response message
        var response = ConstructNetworkResponse(request, processor);
        return Result<Iso8583Message>.Success(response);
    }

    /// <inheritdoc />
    public Result ValidateFinancialMessageAllowed(Iso8583Message request, ProcessorType processor)
    {
        if (request is null)
            return Result.Failure("Message cannot be null.", "INVALID_MESSAGE");

        // Only gate financial messages (authorization and reversal MTIs)
        if (!IsFinancialMessage(request.Mti))
            return Result.Success();

        if (!_router.IsSignedOn(processor))
        {
            _logger.LogWarning(
                "Financial message (MTI {Mti}) blocked for processor {Processor}: sign-on not completed",
                request.Mti, processor);

            return Result.Failure(
                $"Processor {processor} has not completed sign-on. Financial messages are blocked until successful sign-on exchange.",
                "SIGN_ON_REQUIRED");
        }

        return Result.Success();
    }

    /// <summary>
    /// Constructs a network management response (0810) message with response code "00" (approved).
    /// Copies relevant fields from the request and sets the response MTI and response code.
    /// </summary>
    private Iso8583Message ConstructNetworkResponse(Iso8583Message request, ProcessorType processor)
    {
        var responseFields = new Dictionary<int, string>();

        // Copy fields from the request that should be echoed in the response
        foreach (var field in request.Fields)
        {
            responseFields[field.Key] = field.Value;
        }

        // Set response code "00" (approved) for all network management responses
        responseFields[FieldResponseCode] = ResponseCodeApproved;

        return new Iso8583Message
        {
            Mti = MtiNetworkResponse,
            Fields = responseFields
        };
    }

    /// <summary>
    /// Determines whether the given MTI represents a financial message
    /// (authorization or reversal request/response).
    /// </summary>
    private static bool IsFinancialMessage(string? mti)
    {
        return mti != null && FinancialMtis.Contains(mti);
    }
}
