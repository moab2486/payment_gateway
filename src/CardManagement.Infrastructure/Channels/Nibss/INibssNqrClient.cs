namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Internal abstraction over NIBSS NQR HTTP API calls for testability.
/// </summary>
public interface INibssNqrClient
{
    /// <summary>
    /// Registers a QR code with NIBSS NQR and returns the registration response.
    /// </summary>
    Task<NqrRegistrationResponse> RegisterQrAsync(NqrRegistrationRequest request, CancellationToken ct);
}

/// <summary>
/// Represents a QR code registration request to NIBSS NQR.
/// </summary>
public record NqrRegistrationRequest(
    string MerchantId,
    decimal Amount,
    string TransactionReference,
    string QrType,
    DateTime? ExpiresAtUtc);

/// <summary>
/// Represents a response from NIBSS NQR registration.
/// </summary>
public record NqrRegistrationResponse(
    string ResponseCode,
    string? ResponseMessage,
    string? QrReference);

/// <summary>
/// Represents a payment notification received from NIBSS for a QR code payment.
/// </summary>
public record NqrPaymentNotification(
    string TransactionReference,
    decimal Amount,
    string PayerAccount,
    string NibssReference);
