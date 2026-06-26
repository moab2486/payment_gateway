using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// NIBSS Direct Debit adapter for mandate-based recurring payments.
/// Submits mandate creation with debtor/creditor accounts, amount, frequency, and start/end dates.
/// Supports mandate cancellation through the reversal flow.
/// </summary>
public class DirectDebitAdapter : IChannelAdapter
{
    private readonly INibssDirectDebitClient _directDebitClient;
    private readonly ICircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly DirectDebitOptions _options;
    private readonly ILogger<DirectDebitAdapter> _logger;

    public DirectDebitAdapter(
        INibssDirectDebitClient directDebitClient,
        ICircuitBreakerRegistry circuitBreakerRegistry,
        IOptions<DirectDebitOptions> options,
        ILogger<DirectDebitAdapter> logger)
    {
        _directDebitClient = directDebitClient ?? throw new ArgumentNullException(nameof(directDebitClient));
        _circuitBreakerRegistry = circuitBreakerRegistry ?? throw new ArgumentNullException(nameof(circuitBreakerRegistry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PaymentChannel Channel => PaymentChannel.DirectDebit;

    public async Task<ChannelResult> ProcessAsync(PaymentRequest request, CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.DirectDebit);

        // Respect circuit breaker open state
        if (circuitBreaker.State == CircuitBreakerState.Open)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Direct Debit channel circuit breaker is open");
        }

        try
        {
            var mandateRequest = BuildMandateRequest(request);

            var response = await _directDebitClient.CreateMandateAsync(mandateRequest, ct);

            // Map NIBSS response codes to internal standardized codes
            if (NibssResponseCodes.IsSuccess(response.ResponseCode))
            {
                circuitBreaker.RecordSuccess();
                _logger.LogInformation(
                    "Direct Debit mandate created successfully. NIBSS Ref: {NibssRef}, MandateRef: {MandateRef}",
                    response.NibssReference, mandateRequest.MandateReference);
                return new ChannelResult(true, response.NibssReference, null, null);
            }
            else
            {
                var (errorCode, errorMessage) = NibssResponseCodes.Map(response.ResponseCode);
                circuitBreaker.RecordFailure();
                _logger.LogWarning(
                    "Direct Debit mandate creation failed. Code: {ResponseCode}, Message: {Message}, MandateRef: {MandateRef}",
                    response.ResponseCode, errorMessage, mandateRequest.MandateReference);
                return new ChannelResult(false, response.NibssReference, errorCode, errorMessage);
            }
        }
        catch (CircuitBreakerOpenException)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Direct Debit channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Propagate caller cancellation
        }
        catch (Exception ex)
        {
            var circuitBreakerForError = _circuitBreakerRegistry.GetBreaker(PaymentChannel.DirectDebit);
            circuitBreakerForError.RecordFailure();
            _logger.LogError(ex, "Direct Debit mandate creation encountered an unexpected error for {TransactionReference}",
                request.TransactionReference);
            return new ChannelResult(false, null, "SYSTEM_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    public async Task<ChannelResult> ReverseAsync(string transactionReference, CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.DirectDebit);

        if (circuitBreaker.State == CircuitBreakerState.Open)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Direct Debit channel circuit breaker is open");
        }

        try
        {
            _logger.LogInformation(
                "Cancelling Direct Debit mandate for transaction {TransactionReference}",
                transactionReference);

            var response = await _directDebitClient.CancelMandateAsync(transactionReference, ct);

            if (NibssResponseCodes.IsSuccess(response.ResponseCode))
            {
                circuitBreaker.RecordSuccess();
                _logger.LogInformation(
                    "Direct Debit mandate cancelled successfully. NIBSS Ref: {NibssRef}, TransactionRef: {TransactionRef}",
                    response.NibssReference, transactionReference);
                return new ChannelResult(true, response.NibssReference, null, null);
            }
            else
            {
                var (errorCode, errorMessage) = NibssResponseCodes.Map(response.ResponseCode);
                circuitBreaker.RecordFailure();
                _logger.LogWarning(
                    "Direct Debit mandate cancellation failed. Code: {ResponseCode}, Message: {Message}, TransactionRef: {TransactionRef}",
                    response.ResponseCode, errorMessage, transactionReference);
                return new ChannelResult(false, response.NibssReference, errorCode, errorMessage);
            }
        }
        catch (CircuitBreakerOpenException)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "Direct Debit channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Propagate caller cancellation
        }
        catch (Exception ex)
        {
            circuitBreaker.RecordFailure();
            _logger.LogError(ex, "Direct Debit mandate cancellation encountered an unexpected error for {TransactionReference}",
                transactionReference);
            return new ChannelResult(false, null, "SYSTEM_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    public async Task<HealthStatus> CheckHealthAsync(CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.DirectDebit);
        var state = circuitBreaker.State;

        var isHealthy = state != CircuitBreakerState.Open;
        var details = $"Direct Debit circuit breaker state: {state}";

        return await Task.FromResult(new HealthStatus(isHealthy, details));
    }

    /// <summary>
    /// Builds a NIBSS Direct Debit mandate request from the payment request.
    /// The SourceAccount is used as the DebtorAccount, and DestinationAccount as CreditorAccount.
    /// Mandate metadata (frequency, start/end dates) are encoded in the TransactionReference
    /// in the format: "MandateRef|Frequency|StartDate|EndDate".
    /// </summary>
    private DirectDebitMandateRequest BuildMandateRequest(PaymentRequest request)
    {
        var (mandateReference, frequency, startDate, endDate) = ParseMandateMetadata(request.TransactionReference);

        return new DirectDebitMandateRequest(
            MandateReference: mandateReference,
            DebtorAccount: request.SourceAccount,
            CreditorAccount: request.DestinationAccount,
            Amount: request.Amount.Amount,
            CurrencyCode: request.Amount.CurrencyCode,
            Frequency: frequency,
            StartDate: startDate,
            EndDate: endDate,
            TransactionReference: request.TransactionReference);
    }

    /// <summary>
    /// Parses mandate metadata from the transaction reference.
    /// Expected format: "MandateRef|Frequency|StartDate|EndDate"
    /// If the format doesn't match, uses the full reference as mandate reference with defaults.
    /// </summary>
    internal static (string MandateReference, MandateFrequency Frequency, DateTime StartDate, DateTime EndDate) ParseMandateMetadata(string transactionReference)
    {
        if (string.IsNullOrWhiteSpace(transactionReference))
            return (string.Empty, MandateFrequency.Monthly, DateTime.UtcNow, DateTime.UtcNow.AddYears(1));

        var parts = transactionReference.Split('|');
        if (parts.Length >= 4)
        {
            var mandateRef = parts[0];
            var frequency = Enum.TryParse<MandateFrequency>(parts[1], ignoreCase: true, out var freq)
                ? freq
                : MandateFrequency.Monthly;
            var startDate = DateTime.TryParse(parts[2], out var start)
                ? start.ToUniversalTime()
                : DateTime.UtcNow;
            var endDate = DateTime.TryParse(parts[3], out var end)
                ? end.ToUniversalTime()
                : DateTime.UtcNow.AddYears(1);

            return (mandateRef, frequency, startDate, endDate);
        }

        // Fallback: use transaction reference as mandate reference with defaults
        return (transactionReference, MandateFrequency.Monthly, DateTime.UtcNow, DateTime.UtcNow.AddYears(1));
    }
}
