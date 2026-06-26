using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;
using CardManagement.Infrastructure.Resilience;
using CardManagement.Infrastructure.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Channels.CardSwitch;

/// <summary>
/// Interswitch card switch adapter for Verve card network transactions.
/// Constructs ISO 8583 authorization and reversal messages and transmits them
/// via persistent TCP connections managed by IOutboundConnectionPool.
/// Integrates with the PCI boundary for secure cardholder data handling and
/// the circuit breaker registry for failure detection and cascading failure prevention.
/// </summary>
public class InterswitchAdapter : IChannelAdapter
{
    // ISO 8583 field constants
    private const int FieldPan = 2;
    private const int FieldProcessingCode = 3;
    private const int FieldAmount = 4;
    private const int FieldStan = 11;
    private const int FieldLocalTime = 12;
    private const int FieldLocalDate = 13;
    private const int FieldRetrievalRef = 37;
    private const int FieldAuthCode = 38;
    private const int FieldResponseCode = 39;
    private const int FieldTerminalId = 41;
    private const int FieldMerchantId = 42;
    private const int FieldCurrencyCode = 49;
    private const int FieldOriginalDataElements = 90;

    private const string MtiAuthorizationRequest = "0100";
    private const string MtiAuthorizationResponse = "0110";
    private const string MtiFinancialPresentmentRequest = "0220";
    private const string MtiFinancialPresentmentResponse = "0230";
    private const string MtiReversalRequest = "0400";
    private const string MtiReversalResponse = "0410";

    private const string ProcessingCodePurchase = "000000";
    private const string ResponseCodeApproved = "00";

    /// <summary>
    /// The service name used for PCI boundary allowlist authorization.
    /// Must be registered in the PciSecurity:AllowedServices configuration.
    /// </summary>
    internal const string PciServiceName = "InterswitchAdapter";

    private readonly IOutboundConnectionPool _connectionPool;
    private readonly IIso8583Gateway _iso8583Gateway;
    private readonly ICircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly PciBoundary _pciBoundary;
    private readonly InterswitchOptions _options;
    private readonly ILogger<InterswitchAdapter> _logger;

    private static long _stanCounter = 0;

    public InterswitchAdapter(
        IOutboundConnectionPool connectionPool,
        IIso8583Gateway iso8583Gateway,
        ICircuitBreakerRegistry circuitBreakerRegistry,
        PciBoundary pciBoundary,
        IOptions<InterswitchOptions> options,
        ILogger<InterswitchAdapter> logger)
    {
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _iso8583Gateway = iso8583Gateway ?? throw new ArgumentNullException(nameof(iso8583Gateway));
        _circuitBreakerRegistry = circuitBreakerRegistry ?? throw new ArgumentNullException(nameof(circuitBreakerRegistry));
        _pciBoundary = pciBoundary ?? throw new ArgumentNullException(nameof(pciBoundary));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PaymentChannel Channel => PaymentChannel.Interswitch;

    public async Task<ChannelResult> ProcessAsync(PaymentRequest request, CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.Interswitch);

        if (circuitBreaker.State == CircuitBreakerState.Open)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Interswitch channel circuit breaker is open");
        }

        try
        {
            // De-tokenize cardholder data through PCI boundary for ISO 8583 message construction
            var detokenizeResult = await _pciBoundary.DetokenizeAsync(
                request.SourceAccount, PciServiceName, request.TransactionReference, ct);

            if (!detokenizeResult.IsSuccess)
            {
                if (detokenizeResult.IsUnauthorized)
                {
                    _logger.LogError("PCI boundary denied access to cardholder data for {Service}", PciServiceName);
                    return new ChannelResult(false, null, "PCI_ACCESS_DENIED",
                        "Adapter is not authorized to access cardholder data");
                }

                _logger.LogWarning("Token not found in PCI boundary for transaction {TransactionReference}",
                    request.TransactionReference);
                return new ChannelResult(false, null, "INVALID_TOKEN",
                    "Cardholder data token could not be resolved");
            }

            var message = BuildAuthorizationMessage(request, detokenizeResult.Pan!);

            var response = await SendAndReceiveAsync(message, ct);

            return MapResponse(response, circuitBreaker);
        }
        catch (CircuitBreakerOpenException)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Interswitch channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            circuitBreaker.RecordFailure();
            _logger.LogError(ex, "Interswitch authorization failed for {TransactionReference}",
                request.TransactionReference);
            return new ChannelResult(false, null, "SYSTEM_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    public async Task<ChannelResult> ReverseAsync(string transactionReference, CancellationToken ct)
    {
        // Delegate to the enhanced overload with no original data
        return await ReverseAsync(transactionReference, null, ct);
    }

    /// <summary>
    /// Sends a financial presentment (capture) message (MTI 0220) to Interswitch
    /// to settle a previously authorized transaction.
    /// This is not part of the IChannelAdapter interface — it is specific to card switch adapters.
    /// De-tokenizes cardholder data via the PCI boundary before constructing the ISO 8583 message.
    /// </summary>
    /// <param name="request">The capture request containing original authorization details. Pan field is expected to be a token.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A ChannelResult indicating whether the capture was successful.</returns>
    public async Task<ChannelResult> CaptureAsync(CaptureRequest request, CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.Interswitch);

        if (circuitBreaker.State == CircuitBreakerState.Open)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Interswitch channel circuit breaker is open");
        }

        try
        {
            // De-tokenize cardholder data through PCI boundary for ISO 8583 capture message
            var detokenizeResult = await _pciBoundary.DetokenizeAsync(
                request.Pan, PciServiceName, request.TransactionReference, ct);

            if (!detokenizeResult.IsSuccess)
            {
                if (detokenizeResult.IsUnauthorized)
                {
                    _logger.LogError("PCI boundary denied access to cardholder data for capture by {Service}", PciServiceName);
                    return new ChannelResult(false, null, "PCI_ACCESS_DENIED",
                        "Adapter is not authorized to access cardholder data");
                }

                _logger.LogWarning("Token not found in PCI boundary for capture {TransactionReference}",
                    request.TransactionReference);
                return new ChannelResult(false, null, "INVALID_TOKEN",
                    "Cardholder data token could not be resolved");
            }

            var message = BuildCaptureMessage(request, detokenizeResult.Pan!);

            var response = await SendAndReceiveAsync(message, ct);

            return MapResponse(response, circuitBreaker);
        }
        catch (CircuitBreakerOpenException)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Interswitch channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            circuitBreaker.RecordFailure();
            _logger.LogError(ex, "Interswitch capture failed for {TransactionReference}",
                request.TransactionReference);
            return new ChannelResult(false, null, "SYSTEM_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a reversal (MTI 0400) for a timed-out or failed authorization.
    /// Includes original data elements (Field 90) when the original authorization details are available.
    /// </summary>
    /// <param name="transactionReference">The transaction reference for the reversal.</param>
    /// <param name="originalData">Optional original authorization data for Field 90.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    /// <returns>A ChannelResult indicating whether the reversal was successful.</returns>
    public async Task<ChannelResult> ReverseAsync(string transactionReference, OriginalAuthData? originalData, CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.Interswitch);

        if (circuitBreaker.State == CircuitBreakerState.Open)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Interswitch channel circuit breaker is open");
        }

        try
        {
            var message = BuildReversalMessage(transactionReference, originalData);

            var response = await SendAndReceiveAsync(message, ct);

            return MapResponse(response, circuitBreaker);
        }
        catch (CircuitBreakerOpenException)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Interswitch channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            circuitBreaker.RecordFailure();
            _logger.LogError(ex, "Interswitch reversal failed for {TransactionReference}", transactionReference);
            return new ChannelResult(false, null, "SYSTEM_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    public Task<HealthStatus> CheckHealthAsync(CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.Interswitch);
        var state = circuitBreaker.State;

        var isHealthy = state != CircuitBreakerState.Open;
        var details = $"Interswitch circuit breaker state: {state}";

        return Task.FromResult(new HealthStatus(isHealthy, details));
    }

    /// <summary>
    /// Builds an ISO 8583 authorization request message (MTI 0100) with required fields.
    /// Uses the de-tokenized PAN (obtained via PCI boundary) rather than the tokenized source account.
    /// </summary>
    /// <param name="request">The payment request containing transaction details.</param>
    /// <param name="pan">The de-tokenized PAN obtained from the PCI boundary.</param>
    internal Iso8583Message BuildAuthorizationMessage(PaymentRequest request, string pan)
    {
        var now = DateTime.UtcNow;
        var stan = GenerateStan();

        var fields = new Dictionary<int, string>
        {
            [FieldPan] = pan,
            [FieldProcessingCode] = ProcessingCodePurchase,
            [FieldAmount] = request.Amount.Amount.ToString("D12"),
            [FieldStan] = stan,
            [FieldLocalTime] = now.ToString("HHmmss"),
            [FieldLocalDate] = now.ToString("MMdd"),
            [FieldRetrievalRef] = GenerateRetrievalReference(request.TransactionReference),
            [FieldTerminalId] = _options.TerminalId,
            [FieldMerchantId] = _options.MerchantId,
            [FieldCurrencyCode] = MapCurrencyCode(request.Amount.CurrencyCode)
        };

        return new Iso8583Message
        {
            Mti = MtiAuthorizationRequest,
            Fields = fields
        };
    }

    /// <summary>
    /// Overload that uses request.SourceAccount directly (for backward compatibility in tests).
    /// In production, prefer the overload that accepts a de-tokenized PAN.
    /// </summary>
    internal Iso8583Message BuildAuthorizationMessage(PaymentRequest request)
    {
        return BuildAuthorizationMessage(request, request.SourceAccount);
    }

    /// <summary>
    /// Builds an ISO 8583 financial presentment/capture message (MTI 0220).
    /// Uses the de-tokenized PAN (obtained via PCI boundary) rather than the tokenized value.
    /// </summary>
    /// <param name="request">The capture request containing transaction details.</param>
    /// <param name="pan">The de-tokenized PAN obtained from the PCI boundary.</param>
    internal Iso8583Message BuildCaptureMessage(CaptureRequest request, string pan)
    {
        var now = DateTime.UtcNow;
        var stan = GenerateStan();

        var fields = new Dictionary<int, string>
        {
            [FieldPan] = pan,
            [FieldProcessingCode] = ProcessingCodePurchase,
            [FieldAmount] = request.Amount.ToString("D12"),
            [FieldStan] = stan,
            [FieldLocalTime] = now.ToString("HHmmss"),
            [FieldLocalDate] = now.ToString("MMdd"),
            [FieldRetrievalRef] = GenerateRetrievalReference(request.TransactionReference),
            [FieldAuthCode] = request.OriginalAuthCode,
            [FieldTerminalId] = _options.TerminalId,
            [FieldMerchantId] = _options.MerchantId,
            [FieldCurrencyCode] = request.CurrencyCode
        };

        return new Iso8583Message
        {
            Mti = MtiFinancialPresentmentRequest,
            Fields = fields
        };
    }

    /// <summary>
    /// Overload that uses request.Pan directly (for backward compatibility in tests).
    /// In production, prefer the overload that accepts a de-tokenized PAN.
    /// </summary>
    internal Iso8583Message BuildCaptureMessage(CaptureRequest request)
    {
        return BuildCaptureMessage(request, request.Pan);
    }

    /// <summary>
    /// Builds an ISO 8583 reversal request message (MTI 0400).
    /// When original authorization data is provided (e.g., for timeout scenarios),
    /// includes Field 90 (Original data elements) containing the original MTI, STAN,
    /// date/time, and acquiring institution ID.
    /// </summary>
    internal Iso8583Message BuildReversalMessage(string transactionReference, OriginalAuthData? originalData = null)
    {
        var now = DateTime.UtcNow;
        var stan = GenerateStan();

        var fields = new Dictionary<int, string>
        {
            [FieldProcessingCode] = ProcessingCodePurchase,
            [FieldStan] = stan,
            [FieldLocalTime] = now.ToString("HHmmss"),
            [FieldLocalDate] = now.ToString("MMdd"),
            [FieldRetrievalRef] = GenerateRetrievalReference(transactionReference),
            [FieldTerminalId] = _options.TerminalId,
            [FieldMerchantId] = _options.MerchantId
        };

        // Include original data elements (Field 90) for timeout/failed authorization reversals
        if (originalData != null)
        {
            // Field 90 format: Original MTI (4) + Original STAN (6) + Original Date/Time (10) + Acquiring Institution ID (11, zero-padded)
            var originalDataValue = string.Concat(
                originalData.OriginalMti.PadRight(4, '0'),
                originalData.OriginalStan.PadLeft(6, '0'),
                originalData.OriginalDateTime.PadRight(10, '0'),
                originalData.AcquiringInstitutionId.PadLeft(11, '0'));

            fields[FieldOriginalDataElements] = originalDataValue;
        }

        return new Iso8583Message
        {
            Mti = MtiReversalRequest,
            Fields = fields
        };
    }

    /// <summary>
    /// Sends an ISO 8583 message to the Interswitch switch and reads the response.
    /// Uses the IOutboundConnectionPool for persistent TCP/TLS connection management
    /// with automatic reconnection on disconnection.
    /// </summary>
    private async Task<Iso8583Message> SendAndReceiveAsync(Iso8583Message message, CancellationToken ct)
    {
        var endpoint = new ProcessorEndpoint
        {
            Address = _options.Host,
            Port = _options.Port,
            ProcessorType = ProcessorType.Interswitch
        };

        var constructResult = _iso8583Gateway.Construct(message);
        if (!constructResult.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to construct ISO 8583 message: {constructResult.ErrorMessage}");
        }

        var messageBytes = constructResult.Value!;

        // Prepend 2-byte length header (big-endian) for ISO 8583 framing
        var framedMessage = new byte[2 + messageBytes.Length];
        framedMessage[0] = (byte)(messageBytes.Length >> 8);
        framedMessage[1] = (byte)(messageBytes.Length & 0xFF);
        Buffer.BlockCopy(messageBytes, 0, framedMessage, 2, messageBytes.Length);

        // Acquire a bidirectional connection from the pool (with TLS if configured)
        var connection = await _connectionPool.AcquireConnectionAsync(endpoint, _options.TlsEnabled, ct);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(_options.TimeoutMs);

            var stream = connection.Stream;

            // Write the framed ISO 8583 message
            await stream.WriteAsync(framedMessage, timeoutCts.Token);
            await stream.FlushAsync(timeoutCts.Token);

            _logger.LogDebug("ISO 8583 message sent to Interswitch ({Mti}), awaiting response", message.Mti);

            // Read the 2-byte length header from the response
            var lengthHeader = new byte[2];
            await ReadExactAsync(stream, lengthHeader, timeoutCts.Token);

            int responseLength = (lengthHeader[0] << 8) | lengthHeader[1];

            if (!_iso8583Gateway.IsValidFrameSize(responseLength))
            {
                throw new InvalidOperationException(
                    $"Invalid ISO 8583 response frame size: {responseLength} bytes");
            }

            // Read the response body
            var responseBytes = new byte[responseLength];
            await ReadExactAsync(stream, responseBytes, timeoutCts.Token);

            // Parse the ISO 8583 response
            var parseResult = _iso8583Gateway.Parse(responseBytes);
            if (!parseResult.IsSuccess)
            {
                _logger.LogError(
                    "Failed to parse ISO 8583 response from Interswitch: {Error}",
                    parseResult.ErrorMessage);

                return new Iso8583Message
                {
                    Mti = MapResponseMti(message.Mti),
                    Fields = new Dictionary<int, string>
                    {
                        [FieldResponseCode] = "96" // System malfunction
                    }
                };
            }

            _logger.LogDebug(
                "Received ISO 8583 response from Interswitch ({Mti})",
                parseResult.Value!.Mti);

            return parseResult.Value!;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout waiting for response — connection may be stale
            _logger.LogWarning(
                "Timeout waiting for Interswitch response ({TimeoutMs}ms). Discarding connection.",
                _options.TimeoutMs);

            // Mark connection as unhealthy by disposing before release
            await connection.DisposeAsync();
            connection = null!;

            return new Iso8583Message
            {
                Mti = MapResponseMti(message.Mti),
                Fields = new Dictionary<int, string>
                {
                    [FieldResponseCode] = "68" // Response received too late
                }
            };
        }
        finally
        {
            // Return connection to pool if still valid
            if (connection != null!)
            {
                await _connectionPool.ReleaseConnectionAsync(endpoint, connection);
            }
        }
    }

    /// <summary>
    /// Reads exactly the specified number of bytes from the stream.
    /// Handles partial reads by looping until the buffer is filled.
    /// </summary>
    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset), ct);

            if (bytesRead == 0)
            {
                throw new IOException(
                    $"Connection closed by remote host. Expected {buffer.Length} bytes, received {offset}.");
            }

            offset += bytesRead;
        }
    }

    /// <summary>
    /// Maps the ISO 8583 response into a ChannelResult, recording success/failure on the circuit breaker.
    /// Response code "00" indicates approval; all other codes indicate failure.
    /// </summary>
    private ChannelResult MapResponse(Iso8583Message response, ICircuitBreaker circuitBreaker)
    {
        var responseCode = response.Fields.TryGetValue(FieldResponseCode, out var code) ? code : "96";
        var authCode = response.Fields.TryGetValue(FieldAuthCode, out var auth) ? auth : null;

        if (responseCode == ResponseCodeApproved)
        {
            circuitBreaker.RecordSuccess();
            _logger.LogInformation("Interswitch authorization approved. AuthCode: {AuthCode}", authCode);
            return new ChannelResult(true, authCode, null, null);
        }
        else
        {
            circuitBreaker.RecordFailure();
            var errorMessage = MapResponseCodeToMessage(responseCode);
            _logger.LogWarning("Interswitch authorization declined. ResponseCode: {ResponseCode}, Message: {Message}",
                responseCode, errorMessage);
            return new ChannelResult(false, authCode, responseCode, errorMessage);
        }
    }

    /// <summary>
    /// Maps a request MTI to the corresponding response MTI.
    /// </summary>
    private static string MapResponseMti(string requestMti)
    {
        return requestMti switch
        {
            MtiAuthorizationRequest => MtiAuthorizationResponse,
            MtiFinancialPresentmentRequest => MtiFinancialPresentmentResponse,
            MtiReversalRequest => MtiReversalResponse,
            _ => MtiAuthorizationResponse
        };
    }

    /// <summary>
    /// Generates a 6-digit System Trace Audit Number (STAN) that wraps around at 999999.
    /// </summary>
    internal static string GenerateStan()
    {
        var stan = Interlocked.Increment(ref _stanCounter) % 1000000;
        return stan.ToString("D6");
    }

    /// <summary>
    /// Generates a 12-character retrieval reference number from the transaction reference.
    /// Takes the last 12 characters or pads with zeros if shorter.
    /// </summary>
    internal static string GenerateRetrievalReference(string transactionReference)
    {
        if (string.IsNullOrEmpty(transactionReference))
            return "000000000000";

        // Remove non-alphanumeric characters and take last 12
        var cleaned = new string(transactionReference.Where(char.IsLetterOrDigit).ToArray());
        if (cleaned.Length >= 12)
            return cleaned[^12..];

        return cleaned.PadLeft(12, '0');
    }

    /// <summary>
    /// Maps a 3-letter ISO 4217 currency code to the 3-digit numeric code for ISO 8583.
    /// </summary>
    private static string MapCurrencyCode(string currencyCode)
    {
        return currencyCode.ToUpperInvariant() switch
        {
            "NGN" => "566",
            "USD" => "840",
            "GBP" => "826",
            "EUR" => "978",
            _ => "566" // Default to NGN for Nigerian switch
        };
    }

    /// <summary>
    /// Maps ISO 8583 response codes to human-readable messages.
    /// </summary>
    private static string MapResponseCodeToMessage(string responseCode)
    {
        return responseCode switch
        {
            "00" => "Approved",
            "01" => "Refer to card issuer",
            "03" => "Invalid merchant",
            "05" => "Do not honour",
            "12" => "Invalid transaction",
            "13" => "Invalid amount",
            "14" => "Invalid card number",
            "25" => "Unable to locate record on file",
            "30" => "Format error",
            "41" => "Lost card",
            "43" => "Stolen card",
            "51" => "Insufficient funds",
            "54" => "Expired card",
            "55" => "Incorrect PIN",
            "56" => "No card record",
            "57" => "Transaction not permitted to cardholder",
            "58" => "Transaction not permitted to terminal",
            "61" => "Exceeds withdrawal amount limit",
            "65" => "Exceeds withdrawal frequency limit",
            "68" => "Response received too late",
            "75" => "Allowable PIN tries exceeded",
            "91" => "Issuer or switch inoperative",
            "96" => "System malfunction",
            _ => $"Declined (code: {responseCode})"
        };
    }
}
