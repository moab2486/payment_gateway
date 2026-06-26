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
/// Cardify card switch adapter for Visa and Mastercard network transactions.
/// Constructs ISO 8583 authorization, capture, and reversal messages and transmits them
/// via persistent TCP connections managed by IOutboundConnectionPool.
/// Handles Visa and Mastercard message format variations (different processing codes
/// and field mappings based on BIN range).
/// Integrates with the PCI boundary for secure cardholder data handling and
/// the circuit breaker registry for failure detection and cascading failure prevention.
/// </summary>
public class CardifyAdapter : IChannelAdapter
{
    // ISO 8583 field constants
    private const int FieldPan = 2;
    private const int FieldProcessingCode = 3;
    private const int FieldAmount = 4;
    private const int FieldStan = 11;
    private const int FieldLocalTime = 12;
    private const int FieldLocalDate = 13;
    private const int FieldExpiryDate = 14;
    private const int FieldPosEntryMode = 22;
    private const int FieldNetworkId = 24;
    private const int FieldPosConditionCode = 25;
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

    // Visa processing codes
    private const string VisaProcessingCodePurchase = "003000";
    private const string VisaProcessingCodeCapture = "003000";

    // Mastercard processing codes
    private const string MastercardProcessingCodePurchase = "000000";
    private const string MastercardProcessingCodeCapture = "000000";

    // Network identifiers for Field 24
    private const string VisaNetworkId = "0002";
    private const string MastercardNetworkId = "0005";

    private const string ResponseCodeApproved = "00";

    /// <summary>
    /// The service name used for PCI boundary allowlist authorization.
    /// Must be registered in the PciSecurity:AllowedServices configuration.
    /// </summary>
    internal const string PciServiceName = "CardifyAdapter";

    private readonly IOutboundConnectionPool _connectionPool;
    private readonly IIso8583Gateway _iso8583Gateway;
    private readonly ICircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly PciBoundary _pciBoundary;
    private readonly CardifyOptions _options;
    private readonly ILogger<CardifyAdapter> _logger;

    private static long _stanCounter = 0;

    public CardifyAdapter(
        IOutboundConnectionPool connectionPool,
        IIso8583Gateway iso8583Gateway,
        ICircuitBreakerRegistry circuitBreakerRegistry,
        PciBoundary pciBoundary,
        IOptions<CardifyOptions> options,
        ILogger<CardifyAdapter> logger)
    {
        _connectionPool = connectionPool ?? throw new ArgumentNullException(nameof(connectionPool));
        _iso8583Gateway = iso8583Gateway ?? throw new ArgumentNullException(nameof(iso8583Gateway));
        _circuitBreakerRegistry = circuitBreakerRegistry ?? throw new ArgumentNullException(nameof(circuitBreakerRegistry));
        _pciBoundary = pciBoundary ?? throw new ArgumentNullException(nameof(pciBoundary));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PaymentChannel Channel => PaymentChannel.Cardify;

    public async Task<ChannelResult> ProcessAsync(PaymentRequest request, CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.Cardify);

        if (circuitBreaker.State == CircuitBreakerState.Open)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Cardify channel circuit breaker is open");
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

            var pan = detokenizeResult.Pan!;
            var cardScheme = DetectCardScheme(pan);
            var message = BuildAuthorizationMessage(request, pan, cardScheme);

            var response = await SendAndReceiveAsync(message, ct);

            return MapResponse(response, circuitBreaker);
        }
        catch (CircuitBreakerOpenException)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Cardify channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            circuitBreaker.RecordFailure();
            _logger.LogError(ex, "Cardify authorization failed for {TransactionReference}",
                request.TransactionReference);
            return new ChannelResult(false, null, "SYSTEM_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    public async Task<ChannelResult> ReverseAsync(string transactionReference, CancellationToken ct)
    {
        return await ReverseAsync(transactionReference, null, ct);
    }

    /// <summary>
    /// Sends a financial presentment (capture) message (MTI 0220) to Cardify
    /// to settle a previously authorized transaction.
    /// De-tokenizes cardholder data via the PCI boundary before constructing the ISO 8583 message.
    /// Applies Visa/Mastercard format variations based on card BIN.
    /// </summary>
    public async Task<ChannelResult> CaptureAsync(CaptureRequest request, CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.Cardify);

        if (circuitBreaker.State == CircuitBreakerState.Open)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Cardify channel circuit breaker is open");
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

            var pan = detokenizeResult.Pan!;
            var cardScheme = DetectCardScheme(pan);
            var message = BuildCaptureMessage(request, pan, cardScheme);

            var response = await SendAndReceiveAsync(message, ct);

            return MapResponse(response, circuitBreaker);
        }
        catch (CircuitBreakerOpenException)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Cardify channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            circuitBreaker.RecordFailure();
            _logger.LogError(ex, "Cardify capture failed for {TransactionReference}",
                request.TransactionReference);
            return new ChannelResult(false, null, "SYSTEM_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends a reversal (MTI 0400) for a timed-out or failed authorization.
    /// Includes original data elements (Field 90) when original authorization details are available.
    /// </summary>
    public async Task<ChannelResult> ReverseAsync(string transactionReference, OriginalAuthData? originalData, CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.Cardify);

        if (circuitBreaker.State == CircuitBreakerState.Open)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Cardify channel circuit breaker is open");
        }

        try
        {
            var message = BuildReversalMessage(transactionReference, originalData);

            var response = await SendAndReceiveAsync(message, ct);

            return MapResponse(response, circuitBreaker);
        }
        catch (CircuitBreakerOpenException)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Cardify channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            circuitBreaker.RecordFailure();
            _logger.LogError(ex, "Cardify reversal failed for {TransactionReference}", transactionReference);
            return new ChannelResult(false, null, "SYSTEM_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    public Task<HealthStatus> CheckHealthAsync(CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.Cardify);
        var state = circuitBreaker.State;

        var isHealthy = state != CircuitBreakerState.Open;
        var details = $"Cardify circuit breaker state: {state}";

        return Task.FromResult(new HealthStatus(isHealthy, details));
    }

    /// <summary>
    /// Detects the card scheme (Visa or Mastercard) based on the BIN (first 6 digits of PAN).
    /// Visa: BIN starts with 4.
    /// Mastercard: BIN starts with 5 (51-55) or 2 (2221-2720).
    /// </summary>
    internal static CardScheme DetectCardScheme(string pan)
    {
        if (string.IsNullOrEmpty(pan) || pan.Length < 1)
            return CardScheme.Unknown;

        var firstDigit = pan[0];

        if (firstDigit == '4')
            return CardScheme.Visa;

        if (firstDigit == '5' && pan.Length >= 2)
        {
            var secondDigit = pan[1];
            if (secondDigit >= '1' && secondDigit <= '5')
                return CardScheme.Mastercard;
        }

        if (firstDigit == '2' && pan.Length >= 4)
        {
            if (int.TryParse(pan[..4], out var prefix))
            {
                if (prefix >= 2221 && prefix <= 2720)
                    return CardScheme.Mastercard;
            }
        }

        return CardScheme.Unknown;
    }

    /// <summary>
    /// Builds an ISO 8583 authorization request message (MTI 0100) with Visa/Mastercard-specific
    /// field mappings and processing codes based on the detected card scheme.
    /// </summary>
    internal Iso8583Message BuildAuthorizationMessage(PaymentRequest request, string pan, CardScheme cardScheme)
    {
        var now = DateTime.UtcNow;
        var stan = GenerateStan();

        var processingCode = cardScheme == CardScheme.Visa
            ? VisaProcessingCodePurchase
            : MastercardProcessingCodePurchase;

        var fields = new Dictionary<int, string>
        {
            [FieldPan] = pan,
            [FieldProcessingCode] = processingCode,
            [FieldAmount] = request.Amount.Amount.ToString("D12"),
            [FieldStan] = stan,
            [FieldLocalTime] = now.ToString("HHmmss"),
            [FieldLocalDate] = now.ToString("MMdd"),
            [FieldRetrievalRef] = GenerateRetrievalReference(request.TransactionReference),
            [FieldTerminalId] = _options.TerminalId,
            [FieldMerchantId] = _options.MerchantId,
            [FieldCurrencyCode] = MapCurrencyCode(request.Amount.CurrencyCode)
        };

        // Add network-specific fields
        if (cardScheme == CardScheme.Visa)
        {
            fields[FieldNetworkId] = VisaNetworkId;
            fields[FieldPosEntryMode] = "051"; // Visa chip-capable terminal
            fields[FieldPosConditionCode] = "00"; // Normal presentment
        }
        else if (cardScheme == CardScheme.Mastercard)
        {
            fields[FieldNetworkId] = MastercardNetworkId;
            fields[FieldPosEntryMode] = "071"; // Mastercard contactless entry
            fields[FieldPosConditionCode] = "00"; // Normal presentment
        }

        return new Iso8583Message
        {
            Mti = MtiAuthorizationRequest,
            Fields = fields
        };
    }

    /// <summary>
    /// Overload that auto-detects card scheme from PAN (for convenience/testing).
    /// </summary>
    internal Iso8583Message BuildAuthorizationMessage(PaymentRequest request, string pan)
    {
        return BuildAuthorizationMessage(request, pan, DetectCardScheme(pan));
    }

    /// <summary>
    /// Builds an ISO 8583 financial presentment/capture message (MTI 0220)
    /// with scheme-specific processing codes and field mappings.
    /// </summary>
    internal Iso8583Message BuildCaptureMessage(CaptureRequest request, string pan, CardScheme cardScheme)
    {
        var now = DateTime.UtcNow;
        var stan = GenerateStan();

        var processingCode = cardScheme == CardScheme.Visa
            ? VisaProcessingCodeCapture
            : MastercardProcessingCodeCapture;

        var fields = new Dictionary<int, string>
        {
            [FieldPan] = pan,
            [FieldProcessingCode] = processingCode,
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

        // Add network-specific fields for capture
        if (cardScheme == CardScheme.Visa)
        {
            fields[FieldNetworkId] = VisaNetworkId;
        }
        else if (cardScheme == CardScheme.Mastercard)
        {
            fields[FieldNetworkId] = MastercardNetworkId;
        }

        return new Iso8583Message
        {
            Mti = MtiFinancialPresentmentRequest,
            Fields = fields
        };
    }

    /// <summary>
    /// Overload that auto-detects card scheme from PAN.
    /// </summary>
    internal Iso8583Message BuildCaptureMessage(CaptureRequest request, string pan)
    {
        return BuildCaptureMessage(request, pan, DetectCardScheme(pan));
    }

    /// <summary>
    /// Builds an ISO 8583 reversal request message (MTI 0400).
    /// Includes original data elements (Field 90) when available.
    /// </summary>
    internal Iso8583Message BuildReversalMessage(string transactionReference, OriginalAuthData? originalData = null)
    {
        var now = DateTime.UtcNow;
        var stan = GenerateStan();

        var fields = new Dictionary<int, string>
        {
            [FieldProcessingCode] = MastercardProcessingCodePurchase, // Default; reversals use generic code
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
    /// Sends an ISO 8583 message to the Cardify switch and reads the response.
    /// Uses the IOutboundConnectionPool for persistent TCP/TLS connection management
    /// with automatic reconnection on disconnection.
    /// </summary>
    private async Task<Iso8583Message> SendAndReceiveAsync(Iso8583Message message, CancellationToken ct)
    {
        var endpoint = new ProcessorEndpoint
        {
            Address = _options.Host,
            Port = _options.Port,
            ProcessorType = ProcessorType.Cardify
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

            _logger.LogDebug("ISO 8583 message sent to Cardify ({Mti}), awaiting response", message.Mti);

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
                    "Failed to parse ISO 8583 response from Cardify: {Error}",
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
                "Received ISO 8583 response from Cardify ({Mti})",
                parseResult.Value!.Mti);

            return parseResult.Value!;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Timeout waiting for response — connection may be stale
            _logger.LogWarning(
                "Timeout waiting for Cardify response ({TimeoutMs}ms). Discarding connection.",
                _options.TimeoutMs);

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
            if (connection != null!)
            {
                await _connectionPool.ReleaseConnectionAsync(endpoint, connection);
            }
        }
    }

    /// <summary>
    /// Reads exactly the specified number of bytes from the stream.
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
    /// </summary>
    private ChannelResult MapResponse(Iso8583Message response, ICircuitBreaker circuitBreaker)
    {
        var responseCode = response.Fields.TryGetValue(FieldResponseCode, out var code) ? code : "96";
        var authCode = response.Fields.TryGetValue(FieldAuthCode, out var auth) ? auth : null;

        if (responseCode == ResponseCodeApproved)
        {
            circuitBreaker.RecordSuccess();
            _logger.LogInformation("Cardify authorization approved. AuthCode: {AuthCode}", authCode);
            return new ChannelResult(true, authCode, null, null);
        }
        else
        {
            circuitBreaker.RecordFailure();
            var errorMessage = MapResponseCodeToMessage(responseCode);
            _logger.LogWarning("Cardify authorization declined. ResponseCode: {ResponseCode}, Message: {Message}",
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
    /// </summary>
    internal static string GenerateRetrievalReference(string transactionReference)
    {
        if (string.IsNullOrEmpty(transactionReference))
            return "000000000000";

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
            _ => "566" // Default to NGN
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

/// <summary>
/// Represents the card scheme (brand) detected from the PAN's BIN range.
/// </summary>
public enum CardScheme
{
    /// <summary>Card scheme could not be determined from BIN.</summary>
    Unknown,
    /// <summary>Visa card (BIN starts with 4).</summary>
    Visa,
    /// <summary>Mastercard (BIN starts with 51-55 or 2221-2720).</summary>
    Mastercard
}
