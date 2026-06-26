using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// NIBSS Instant Payment (NIP) adapter for real-time interbank fund transfers.
/// Validates bank codes and accounts, submits transfers with configurable timeout,
/// and initiates status inquiry on timeout before reporting outcome.
/// </summary>
public class NipAdapter : IChannelAdapter
{
    private readonly INibssNipClient _nibssClient;
    private readonly ICircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly NipOptions _options;
    private readonly ILogger<NipAdapter> _logger;

    public NipAdapter(
        INibssNipClient nibssClient,
        ICircuitBreakerRegistry circuitBreakerRegistry,
        IOptions<NipOptions> options,
        ILogger<NipAdapter> logger)
    {
        _nibssClient = nibssClient ?? throw new ArgumentNullException(nameof(nibssClient));
        _circuitBreakerRegistry = circuitBreakerRegistry ?? throw new ArgumentNullException(nameof(circuitBreakerRegistry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PaymentChannel Channel => PaymentChannel.NIP;

    public async Task<ChannelResult> ProcessAsync(PaymentRequest request, CancellationToken ct)
    {
        // Validate destination bank code and account
        var validationError = ValidateDestination(request.DestinationAccount);
        if (validationError is not null)
        {
            return new ChannelResult(false, null, "VALIDATION_ERROR", validationError);
        }

        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.NIP);

        // Respect circuit breaker open state
        if (circuitBreaker.State == CircuitBreakerState.Open)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "NIP channel circuit breaker is open");
        }

        try
        {
            var transferRequest = BuildTransferRequest(request);

            NibssTransferResponse response;
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(_options.TimeoutMs);

                response = await _nibssClient.SubmitTransferAsync(transferRequest, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Timeout occurred — initiate status inquiry
                _logger.LogWarning("NIP transfer timed out for {TransactionReference}. Initiating status inquiry.",
                    request.TransactionReference);

                response = await PerformStatusInquiry(request.TransactionReference, ct);
            }

            // Map response
            if (NibssResponseCodes.IsSuccess(response.ResponseCode))
            {
                circuitBreaker.RecordSuccess();
                _logger.LogInformation("NIP transfer successful. NIBSS Ref: {NibssRef}", response.NibssReference);
                return new ChannelResult(true, response.NibssReference, null, null);
            }
            else
            {
                var (errorCode, errorMessage) = NibssResponseCodes.Map(response.ResponseCode);
                circuitBreaker.RecordFailure();
                _logger.LogWarning("NIP transfer failed. Code: {ResponseCode}, Message: {Message}",
                    response.ResponseCode, errorMessage);
                return new ChannelResult(false, response.NibssReference, errorCode, errorMessage);
            }
        }
        catch (CircuitBreakerOpenException)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "NIP channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Propagate caller cancellation
        }
        catch (Exception ex)
        {
            circuitBreaker.RecordFailure();
            _logger.LogError(ex, "NIP transfer encountered an unexpected error for {TransactionReference}",
                request.TransactionReference);
            return new ChannelResult(false, null, "SYSTEM_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    public async Task<ChannelResult> ReverseAsync(string transactionReference, CancellationToken ct)
    {
        // NIP transfers are real-time and generally non-reversible at the NIBSS level.
        // Return a not-supported indication.
        _logger.LogWarning("NIP reversal requested for {TransactionReference}. NIP does not support reversals.",
            transactionReference);
        return await Task.FromResult(
            new ChannelResult(false, null, "NOT_SUPPORTED", "NIP does not support transaction reversals"));
    }

    public async Task<HealthStatus> CheckHealthAsync(CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.NIP);
        var state = circuitBreaker.State;

        var isHealthy = state != CircuitBreakerState.Open;
        var details = $"NIP circuit breaker state: {state}";

        return await Task.FromResult(new HealthStatus(isHealthy, details));
    }

    /// <summary>
    /// Validates the destination account string which should contain a bank code and account number
    /// in the format "BankCode:AccountNumber" (e.g., "044:0123456789").
    /// Bank code must be 3 digits; account number must be 10 digits.
    /// </summary>
    internal static string? ValidateDestination(string destinationAccount)
    {
        if (string.IsNullOrWhiteSpace(destinationAccount))
            return "Destination account is required";

        var parts = destinationAccount.Split(':');
        if (parts.Length != 2)
            return "Destination account must be in format 'BankCode:AccountNumber' (e.g., '044:0123456789')";

        var bankCode = parts[0].Trim();
        var accountNumber = parts[1].Trim();

        if (!IsValidBankCode(bankCode))
            return $"Invalid bank code '{bankCode}'. Must be a 3-digit numeric code";

        if (!IsValidAccountNumber(accountNumber))
            return $"Invalid account number. Must be exactly 10 digits";

        return null;
    }

    /// <summary>
    /// Bank code validation: must be exactly 3 digits.
    /// </summary>
    internal static bool IsValidBankCode(string bankCode)
    {
        return bankCode.Length == 3 && bankCode.All(char.IsDigit);
    }

    /// <summary>
    /// Account number validation: must be exactly 10 digits.
    /// </summary>
    internal static bool IsValidAccountNumber(string accountNumber)
    {
        return accountNumber.Length == 10 && accountNumber.All(char.IsDigit);
    }

    private NibssTransferRequest BuildTransferRequest(PaymentRequest request)
    {
        // Parse source and destination in format "BankCode:AccountNumber"
        var (senderBankCode, senderAccount) = ParseBankAccount(request.SourceAccount);
        var (recipientBankCode, recipientAccount) = ParseBankAccount(request.DestinationAccount);

        // Convert from smallest currency unit (kobo) to decimal amount
        var amount = request.Amount.Amount / 100m;

        return new NibssTransferRequest(
            SessionId: request.TransactionReference,
            SenderName: "Sender", // In a real scenario, this would come from account lookup
            SenderAccount: senderAccount,
            SenderBankCode: senderBankCode,
            RecipientName: "Recipient", // In a real scenario, this would come from name inquiry
            RecipientAccount: recipientAccount,
            RecipientBankCode: recipientBankCode,
            Amount: amount,
            CurrencyCode: request.Amount.CurrencyCode,
            Narration: $"Transfer {request.TransactionReference}");
    }

    private static (string BankCode, string AccountNumber) ParseBankAccount(string account)
    {
        var parts = account.Split(':');
        if (parts.Length == 2)
            return (parts[0].Trim(), parts[1].Trim());

        // Fallback: treat entire string as account number with empty bank code
        return (string.Empty, account);
    }

    private async Task<NibssTransferResponse> PerformStatusInquiry(string transactionReference, CancellationToken ct)
    {
        // Wait for configured delay before checking status
        if (_options.StatusInquiryDelayMs > 0)
        {
            await Task.Delay(_options.StatusInquiryDelayMs, ct);
        }

        try
        {
            var statusResponse = await _nibssClient.QueryStatusAsync(transactionReference, ct);
            _logger.LogInformation("NIP status inquiry for {TransactionReference} returned code: {ResponseCode}",
                transactionReference, statusResponse.ResponseCode);
            return statusResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NIP status inquiry failed for {TransactionReference}", transactionReference);
            return new NibssTransferResponse("96", "Status inquiry failed", null);
        }
    }
}
