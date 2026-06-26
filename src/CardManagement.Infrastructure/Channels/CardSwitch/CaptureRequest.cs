namespace CardManagement.Infrastructure.Channels.CardSwitch;

/// <summary>
/// Represents a capture/financial presentment request for a previously authorized card transaction.
/// Contains the original authorization details needed to construct the MTI 0220 message.
/// </summary>
public record CaptureRequest
{
    /// <summary>
    /// The primary account number (PAN) for the cardholder.
    /// </summary>
    public string Pan { get; init; } = string.Empty;

    /// <summary>
    /// The transaction amount formatted as a 12-digit integer (in minor units).
    /// </summary>
    public long Amount { get; init; }

    /// <summary>
    /// The transaction reference used to correlate with the original authorization.
    /// </summary>
    public string TransactionReference { get; init; } = string.Empty;

    /// <summary>
    /// The authorization code received in the original authorization response (Field 38).
    /// </summary>
    public string OriginalAuthCode { get; init; } = string.Empty;

    /// <summary>
    /// The ISO 4217 numeric currency code (e.g., "566" for NGN).
    /// </summary>
    public string CurrencyCode { get; init; } = "566";
}
