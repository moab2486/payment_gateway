using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// NIBSS mCash/USSD adapter for mobile payments via USSD channels.
/// Generates session identifiers, submits payment instructions with PIN-based auth,
/// and handles USSD session timeouts with status inquiry fallback.
/// </summary>
public class MCashAdapter : IChannelAdapter
{
    private readonly INibssMCashClient _mcashClient;
    private readonly ICircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly MCashOptions _options;
    private readonly ILogger<MCashAdapter> _logger;

    public MCashAdapter(
        INibssMCashClient mcashClient,
        ICircuitBreakerRegistry circuitBreakerRegistry,
        IOptions<MCashOptions> options,
        ILogger<MCashAdapter> logger)
    {
        _mcashClient = mcashClient ?? throw new ArgumentNullException(nameof(mcashClient));
        _circuitBreakerRegistry = circuitBreakerRegistry ?? throw new ArgumentNullException(nameof(circuitBreakerRegistry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PaymentChannel Channel => PaymentChannel.MCash;

    public async Task<ChannelResult> ProcessAsync(PaymentRequest request, CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.MCash);

        // Respect circuit breaker open state
        if (circuitBreaker.State == CircuitBreakerState.Open)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "mCash channel circuit breaker is open");
        }

        try
        {
            // Generate unique session identifier for this USSD session
            var sessionId = GenerateSessionId();

            var mcashRequest = BuildMCashRequest(request, sessionId);

            MCashResponse response;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_options.SessionTimeoutMs);

                response = await _mcashClient.SubmitPaymentAsync(mcashRequest, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // USSD session timed out — mark as timed-out and initiate status inquiry
                _logger.LogWarning(
                    "mCash USSD session timed out for {TransactionReference} (SessionId: {SessionId}). Initiating status inquiry.",
                    request.TransactionReference, sessionId);

                response = await PerformStatusInquiry(sessionId, ct);
            }

            // Map NIBSS mCash response codes to internal standardized codes
            if (NibssResponseCodes.IsSuccess(response.ResponseCode))
            {
                circuitBreaker.RecordSuccess();
                _logger.LogInformation(
                    "mCash payment successful. NIBSS Ref: {NibssRef}, SessionId: {SessionId}",
                    response.NibssReference, sessionId);
                return new ChannelResult(true, response.NibssReference, null, null);
            }
            else
            {
                var (errorCode, errorMessage) = MCashResponseCodes.Map(response.ResponseCode);
                circuitBreaker.RecordFailure();
                _logger.LogWarning(
                    "mCash payment failed. Code: {ResponseCode}, Message: {Message}, SessionId: {SessionId}",
                    response.ResponseCode, errorMessage, sessionId);
                return new ChannelResult(false, response.NibssReference, errorCode, errorMessage);
            }
        }
        catch (CircuitBreakerOpenException)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "mCash channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Propagate caller cancellation
        }
        catch (Exception ex)
        {
            circuitBreaker.RecordFailure();
            _logger.LogError(ex, "mCash payment encountered an unexpected error for {TransactionReference}",
                request.TransactionReference);
            return new ChannelResult(false, null, "SYSTEM_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    public async Task<ChannelResult> ReverseAsync(string transactionReference, CancellationToken ct)
    {
        // mCash/USSD payments are generally non-reversible at the NIBSS level.
        _logger.LogWarning("mCash reversal requested for {TransactionReference}. mCash does not support reversals.",
            transactionReference);
        return await Task.FromResult(
            new ChannelResult(false, null, "NOT_SUPPORTED", "mCash does not support transaction reversals"));
    }

    public async Task<HealthStatus> CheckHealthAsync(CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.MCash);
        var state = circuitBreaker.State;

        var isHealthy = state != CircuitBreakerState.Open;
        var details = $"mCash circuit breaker state: {state}";

        return await Task.FromResult(new HealthStatus(isHealthy, details));
    }

    /// <summary>
    /// Generates a unique session identifier for the USSD session.
    /// Uses Guid.NewGuid() in "N" format (32 hex digits, no hyphens).
    /// </summary>
    internal static string GenerateSessionId()
    {
        return Guid.NewGuid().ToString("N");
    }

    private MCashRequest BuildMCashRequest(PaymentRequest request, string sessionId)
    {
        // Convert from smallest currency unit (kobo) to decimal amount
        var amount = request.Amount.Amount / 100m;

        // For mCash, PIN block is passed via the SourceAccount field in the format "PhoneNumber|PinBlock"
        // If no PIN block separator found, use empty PIN block
        var (sourceAccount, pinBlock) = ParseSourceWithPin(request.SourceAccount);

        return new MCashRequest(
            SessionId: sessionId,
            SourceAccount: sourceAccount,
            DestinationAccount: request.DestinationAccount,
            Amount: amount,
            PinBlock: pinBlock,
            TransactionReference: request.TransactionReference);
    }

    /// <summary>
    /// Parses the source account field for mCash.
    /// Format: "PhoneNumber|PinBlock" or just "PhoneNumber" (empty PIN block).
    /// </summary>
    internal static (string SourceAccount, string PinBlock) ParseSourceWithPin(string sourceAccount)
    {
        if (string.IsNullOrWhiteSpace(sourceAccount))
            return (string.Empty, string.Empty);

        var separatorIndex = sourceAccount.IndexOf('|');
        if (separatorIndex < 0)
            return (sourceAccount, string.Empty);

        return (sourceAccount[..separatorIndex], sourceAccount[(separatorIndex + 1)..]);
    }

    private async Task<MCashResponse> PerformStatusInquiry(string sessionId, CancellationToken ct)
    {
        try
        {
            var statusResponse = await _mcashClient.QueryStatusAsync(sessionId, ct);
            _logger.LogInformation(
                "mCash status inquiry for SessionId {SessionId} returned code: {ResponseCode}",
                sessionId, statusResponse.ResponseCode);
            return statusResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "mCash status inquiry failed for SessionId {SessionId}", sessionId);
            // Return timeout response code when status inquiry itself fails
            return new MCashResponse("68", "Status inquiry failed — session timed out", null);
        }
    }
}
