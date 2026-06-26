using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// NIBSS e-BillsPay adapter for electronic bill payment processing.
/// Validates biller codes and customer references against the NIBSS biller directory,
/// enforces biller-defined amount limits, and forwards payment instructions to NIBSS.
/// </summary>
public class EBillsPayAdapter : IChannelAdapter
{
    private readonly INibssEBillsPayClient _ebillsPayClient;
    private readonly ICircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly EBillsPayOptions _options;
    private readonly ILogger<EBillsPayAdapter> _logger;
    private readonly IReadOnlyDictionary<string, BillerInfo> _billerDirectory;

    public EBillsPayAdapter(
        INibssEBillsPayClient ebillsPayClient,
        ICircuitBreakerRegistry circuitBreakerRegistry,
        IOptions<EBillsPayOptions> options,
        ILogger<EBillsPayAdapter> logger,
        IReadOnlyDictionary<string, BillerInfo>? billerDirectory = null)
    {
        _ebillsPayClient = ebillsPayClient ?? throw new ArgumentNullException(nameof(ebillsPayClient));
        _circuitBreakerRegistry = circuitBreakerRegistry ?? throw new ArgumentNullException(nameof(circuitBreakerRegistry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _billerDirectory = billerDirectory ?? new Dictionary<string, BillerInfo>();
    }

    public PaymentChannel Channel => PaymentChannel.EBillsPay;

    public async Task<ChannelResult> ProcessAsync(PaymentRequest request, CancellationToken ct)
    {
        // Parse destination: "BillerCode:CustomerReference"
        var parseResult = ParseDestination(request.DestinationAccount);
        if (parseResult.Error is not null)
        {
            return new ChannelResult(false, null, "VALIDATION_ERROR", parseResult.Error);
        }

        var billerCode = parseResult.BillerCode!;
        var customerReference = parseResult.CustomerReference!;

        // Validate biller code against directory
        if (!_billerDirectory.TryGetValue(billerCode, out var billerInfo))
        {
            return new ChannelResult(false, null, "VALIDATION_ERROR",
                $"Invalid biller code '{billerCode}'. Biller not found in NIBSS directory.");
        }

        // Validate customer reference if the biller requires it
        if (billerInfo.AcceptsCustomerRef && string.IsNullOrWhiteSpace(customerReference))
        {
            return new ChannelResult(false, null, "VALIDATION_ERROR",
                $"Customer reference is required for biller '{billerInfo.BillerName}'.");
        }

        // Convert amount from smallest unit (kobo) to decimal naira
        var amountNaira = request.Amount.Amount / 100m;

        // Validate amount against biller-defined limits
        var amountError = ValidateAmount(amountNaira, billerInfo);
        if (amountError is not null)
        {
            return new ChannelResult(false, null, "VALIDATION_ERROR", amountError);
        }

        // Check circuit breaker
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.EBillsPay);
        if (circuitBreaker.State == CircuitBreakerState.Open)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "e-BillsPay channel circuit breaker is open");
        }

        try
        {
            var paymentRequest = new EBillsPayRequest(
                BillerCode: billerCode,
                CustomerReference: customerReference,
                Amount: amountNaira,
                PayerName: "Payer", // In production, resolved from account lookup
                PayerAccount: request.SourceAccount,
                TransactionReference: request.TransactionReference);

            var response = await _ebillsPayClient.SubmitPaymentAsync(paymentRequest, ct);

            if (NibssResponseCodes.IsSuccess(response.ResponseCode))
            {
                circuitBreaker.RecordSuccess();
                _logger.LogInformation(
                    "e-BillsPay payment successful. Biller: {BillerCode}, Confirmation: {ConfirmationRef}",
                    billerCode, response.ConfirmationReference);
                return new ChannelResult(true, response.ConfirmationReference, null, null);
            }
            else
            {
                var (errorCode, errorMessage) = NibssResponseCodes.Map(response.ResponseCode);
                circuitBreaker.RecordFailure();
                _logger.LogWarning(
                    "e-BillsPay payment failed. Biller: {BillerCode}, Code: {ResponseCode}, Message: {Message}",
                    billerCode, response.ResponseCode, errorMessage);
                return new ChannelResult(false, response.ConfirmationReference, errorCode, errorMessage);
            }
        }
        catch (CircuitBreakerOpenException)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "e-BillsPay channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Propagate caller cancellation
        }
        catch (Exception ex)
        {
            circuitBreaker.RecordFailure();
            _logger.LogError(ex, "e-BillsPay payment encountered an unexpected error for {TransactionReference}",
                request.TransactionReference);
            return new ChannelResult(false, null, "SYSTEM_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    public Task<ChannelResult> ReverseAsync(string transactionReference, CancellationToken ct)
    {
        _logger.LogWarning(
            "e-BillsPay reversal requested for {TransactionReference}. e-BillsPay does not support reversals.",
            transactionReference);
        return Task.FromResult(
            new ChannelResult(false, null, "NOT_SUPPORTED", "e-BillsPay does not support transaction reversals"));
    }

    public Task<HealthStatus> CheckHealthAsync(CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.EBillsPay);
        var state = circuitBreaker.State;

        var isHealthy = state != CircuitBreakerState.Open;
        var details = $"e-BillsPay circuit breaker state: {state}";

        return Task.FromResult(new HealthStatus(isHealthy, details));
    }

    /// <summary>
    /// Parses the DestinationAccount in format "BillerCode:CustomerReference".
    /// </summary>
    internal static (string? BillerCode, string? CustomerReference, string? Error) ParseDestination(string destinationAccount)
    {
        if (string.IsNullOrWhiteSpace(destinationAccount))
            return (null, null, "Destination account is required");

        var parts = destinationAccount.Split(':');
        if (parts.Length != 2)
            return (null, null, "Destination account must be in format 'BillerCode:CustomerReference' (e.g., 'POWER001:METER12345')");

        var billerCode = parts[0].Trim();
        var customerReference = parts[1].Trim();

        if (string.IsNullOrWhiteSpace(billerCode))
            return (null, null, "Biller code cannot be empty");

        if (string.IsNullOrWhiteSpace(customerReference))
            return (null, null, "Customer reference cannot be empty");

        return (billerCode, customerReference, null);
    }

    /// <summary>
    /// Validates the payment amount against biller-defined min/max limits.
    /// Returns null if valid, or an error message if invalid.
    /// </summary>
    internal static string? ValidateAmount(decimal amountNaira, BillerInfo billerInfo)
    {
        if (billerInfo.MinAmount.HasValue && amountNaira < billerInfo.MinAmount.Value)
        {
            return $"Amount {amountNaira:N2} is below the minimum of {billerInfo.MinAmount.Value:N2} for biller '{billerInfo.BillerName}'.";
        }

        if (billerInfo.MaxAmount.HasValue && amountNaira > billerInfo.MaxAmount.Value)
        {
            return $"Amount {amountNaira:N2} is above the maximum of {billerInfo.MaxAmount.Value:N2} for biller '{billerInfo.BillerName}'.";
        }

        return null;
    }
}
