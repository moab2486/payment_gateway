using System.Collections.Concurrent;
using System.Text.Json;
using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.Enums;
using CardManagement.Infrastructure.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// NIBSS Quick Response (NQR) adapter for QR code-based merchant payments.
/// Supports static QR codes (reusable, merchant-level) and dynamic QR codes
/// (single-use, transaction-level with expiry).
/// </summary>
public class NqrAdapter : IChannelAdapter
{
    private readonly INibssNqrClient _nibssClient;
    private readonly ICircuitBreakerRegistry _circuitBreakerRegistry;
    private readonly NqrOptions _options;
    private readonly ILogger<NqrAdapter> _logger;
    private readonly ITimeProvider _timeProvider;

    /// <summary>
    /// In-memory store for QR registrations keyed by TransactionReference.
    /// Used for notification matching and expiry tracking.
    /// </summary>
    private readonly ConcurrentDictionary<string, QrRegistration> _qrRegistrations = new();

    public NqrAdapter(
        INibssNqrClient nibssClient,
        ICircuitBreakerRegistry circuitBreakerRegistry,
        IOptions<NqrOptions> options,
        ILogger<NqrAdapter> logger,
        ITimeProvider? timeProvider = null)
    {
        _nibssClient = nibssClient ?? throw new ArgumentNullException(nameof(nibssClient));
        _circuitBreakerRegistry = circuitBreakerRegistry ?? throw new ArgumentNullException(nameof(circuitBreakerRegistry));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? new SystemTimeProvider();
    }

    public PaymentChannel Channel => PaymentChannel.NQR;

    public async Task<ChannelResult> ProcessAsync(PaymentRequest request, CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.NQR);

        if (circuitBreaker.State == CircuitBreakerState.Open)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "NQR channel circuit breaker is open");
        }

        try
        {
            var qrPayload = GenerateQrPayload(request);
            var registrationRequest = new NqrRegistrationRequest(
                MerchantId: qrPayload.MerchantId,
                Amount: qrPayload.Amount,
                TransactionReference: qrPayload.TransactionReference,
                QrType: qrPayload.QrType,
                ExpiresAtUtc: qrPayload.ExpiresAtUtc);

            var response = await _nibssClient.RegisterQrAsync(registrationRequest, ct);

            if (NibssResponseCodes.IsSuccess(response.ResponseCode))
            {
                // Store QR registration for notification matching
                var registration = new QrRegistration(
                    TransactionReference: request.TransactionReference,
                    MerchantId: qrPayload.MerchantId,
                    Amount: qrPayload.Amount,
                    QrType: qrPayload.QrType,
                    ExpiresAtUtc: qrPayload.ExpiresAtUtc,
                    QrReference: response.QrReference,
                    CreatedAtUtc: _timeProvider.UtcNow);

                _qrRegistrations[request.TransactionReference] = registration;

                circuitBreaker.RecordSuccess();
                _logger.LogInformation(
                    "NQR code generated successfully. Type: {QrType}, Ref: {QrReference}",
                    qrPayload.QrType, response.QrReference);

                // Return the QR payload as the processor reference
                return new ChannelResult(true, response.QrReference ?? qrPayload.TransactionReference, null, null);
            }
            else
            {
                var (errorCode, errorMessage) = NibssResponseCodes.Map(response.ResponseCode);
                circuitBreaker.RecordFailure();
                _logger.LogWarning(
                    "NQR registration failed. Code: {ResponseCode}, Message: {Message}",
                    response.ResponseCode, errorMessage);
                return new ChannelResult(false, null, errorCode, errorMessage);
            }
        }
        catch (CircuitBreakerOpenException)
        {
            return new ChannelResult(false, null, "CHANNEL_UNAVAILABLE", "NQR channel circuit breaker is open");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // Propagate caller cancellation
        }
        catch (Exception ex)
        {
            circuitBreaker.RecordFailure();
            _logger.LogError(ex, "NQR processing encountered an unexpected error for {TransactionReference}",
                request.TransactionReference);
            return new ChannelResult(false, null, "SYSTEM_ERROR", $"Unexpected error: {ex.Message}");
        }
    }

    public Task<ChannelResult> ReverseAsync(string transactionReference, CancellationToken ct)
    {
        // NQR payments are push-based (customer-initiated). Reversals go through dispute flow.
        _logger.LogWarning("NQR reversal requested for {TransactionReference}. NQR does not support direct reversals.",
            transactionReference);
        return Task.FromResult(
            new ChannelResult(false, null, "NOT_SUPPORTED", "NQR does not support transaction reversals"));
    }

    public Task<HealthStatus> CheckHealthAsync(CancellationToken ct)
    {
        var circuitBreaker = _circuitBreakerRegistry.GetBreaker(PaymentChannel.NQR);
        var state = circuitBreaker.State;

        var isHealthy = state != CircuitBreakerState.Open;
        var details = $"NQR circuit breaker state: {state}";

        return Task.FromResult(new HealthStatus(isHealthy, details));
    }

    /// <summary>
    /// Matches an incoming NIBSS payment notification to an originating QR transaction.
    /// Returns a ChannelResult indicating success or failure (including amount mismatch).
    /// </summary>
    public ChannelResult MatchPaymentNotification(NqrPaymentNotification notification)
    {
        if (notification is null)
        {
            return new ChannelResult(false, null, "INVALID_NOTIFICATION", "Notification is null");
        }

        if (!_qrRegistrations.TryGetValue(notification.TransactionReference, out var registration))
        {
            _logger.LogWarning(
                "NQR notification received for unknown transaction reference: {TransactionReference}",
                notification.TransactionReference);
            return new ChannelResult(false, null, "TRANSACTION_NOT_FOUND",
                $"No QR registration found for reference '{notification.TransactionReference}'");
        }

        // Check if dynamic QR has expired
        if (registration.QrType == QrType.Dynamic && registration.ExpiresAtUtc.HasValue)
        {
            if (_timeProvider.UtcNow > registration.ExpiresAtUtc.Value)
            {
                _logger.LogWarning(
                    "NQR notification received for expired dynamic QR. Ref: {TransactionReference}, Expired: {ExpiresAtUtc}",
                    notification.TransactionReference, registration.ExpiresAtUtc);
                return new ChannelResult(false, null, "QR_EXPIRED",
                    $"Dynamic QR code has expired at {registration.ExpiresAtUtc.Value:O}");
            }
        }

        // Validate amount matches for dynamic QR codes (static QR codes have amount = 0)
        if (registration.QrType == QrType.Dynamic && registration.Amount > 0)
        {
            if (notification.Amount != registration.Amount)
            {
                _logger.LogWarning(
                    "NQR amount mismatch. Expected: {ExpectedAmount}, Received: {ReceivedAmount}, Ref: {TransactionReference}",
                    registration.Amount, notification.Amount, notification.TransactionReference);
                return new ChannelResult(false, null, "AMOUNT_MISMATCH",
                    $"Payment amount {notification.Amount} does not match expected amount {registration.Amount}");
            }
        }

        _logger.LogInformation(
            "NQR notification matched successfully. Ref: {TransactionReference}, NIBSS Ref: {NibssReference}",
            notification.TransactionReference, notification.NibssReference);

        // For dynamic QR, remove from store after successful match (single-use)
        if (registration.QrType == QrType.Dynamic)
        {
            _qrRegistrations.TryRemove(notification.TransactionReference, out _);
        }

        return new ChannelResult(true, notification.NibssReference, null, null);
    }

    /// <summary>
    /// Checks if a dynamic QR code has expired based on the configured expiry period.
    /// Returns true if the QR code is expired.
    /// </summary>
    public bool IsDynamicQrExpired(string transactionReference)
    {
        if (!_qrRegistrations.TryGetValue(transactionReference, out var registration))
        {
            return false; // Unknown reference, not expired per se
        }

        if (registration.QrType != QrType.Dynamic || !registration.ExpiresAtUtc.HasValue)
        {
            return false; // Static QR codes don't expire
        }

        return _timeProvider.UtcNow > registration.ExpiresAtUtc.Value;
    }

    /// <summary>
    /// Gets the QR registration for a given transaction reference (for testing/inspection).
    /// </summary>
    internal QrRegistration? GetRegistration(string transactionReference)
    {
        _qrRegistrations.TryGetValue(transactionReference, out var registration);
        return registration;
    }

    /// <summary>
    /// Generates a QR payload compliant with NIBSS NQR specification.
    /// Determines if the QR is static or dynamic based on the amount:
    /// - Amount > 0: Dynamic QR (single-use, transaction-level, has expiry)
    /// - Amount = 0: Static QR (reusable, merchant-level, no expiry)
    /// </summary>
    internal NqrPayload GenerateQrPayload(PaymentRequest request)
    {
        var merchantId = request.DestinationAccount;
        var amount = request.Amount.Amount / 100m; // Convert from kobo to naira
        var qrType = amount > 0 ? QrType.Dynamic : QrType.Static;

        DateTime? expiresAtUtc = null;
        if (qrType == QrType.Dynamic)
        {
            expiresAtUtc = _timeProvider.UtcNow.AddMinutes(_options.DynamicQrExpiryMinutes);
        }

        return new NqrPayload(
            MerchantId: merchantId,
            Amount: amount,
            TransactionReference: request.TransactionReference,
            QrType: qrType == QrType.Dynamic ? "Dynamic" : "Static",
            ExpiresAtUtc: expiresAtUtc);
    }
}

/// <summary>
/// Represents a generated QR code payload compliant with NIBSS NQR specification.
/// </summary>
public record NqrPayload(
    string MerchantId,
    decimal Amount,
    string TransactionReference,
    string QrType,
    DateTime? ExpiresAtUtc);

/// <summary>
/// Represents a stored QR code registration for notification matching.
/// </summary>
public record QrRegistration(
    string TransactionReference,
    string MerchantId,
    decimal Amount,
    string QrType,
    DateTime? ExpiresAtUtc,
    string? QrReference,
    DateTime CreatedAtUtc);

/// <summary>
/// QR code type enumeration.
/// </summary>
public static class QrType
{
    public const string Static = "Static";
    public const string Dynamic = "Dynamic";
}

/// <summary>
/// Abstraction for time to allow testing of expiry logic.
/// </summary>
public interface ITimeProvider
{
    DateTime UtcNow { get; }
}

/// <summary>
/// Default system time provider.
/// </summary>
public class SystemTimeProvider : ITimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
