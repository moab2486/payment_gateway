namespace CardManagement.Domain.PlatformServices.Notifications;

/// <summary>
/// Represents the outcome of a notification delivery attempt via a channel adapter.
/// Captures success/failure status, provider tracking information, and timing.
/// </summary>
public record DeliveryResult
{
    /// <summary>
    /// Whether the delivery was successful.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// The provider-assigned message identifier for tracking purposes.
    /// Null when the delivery failed before the provider assigned an ID.
    /// </summary>
    public string? ProviderMessageId { get; }

    /// <summary>
    /// The reason for delivery failure. Null when delivery succeeded.
    /// </summary>
    public string? FailureReason { get; }

    /// <summary>
    /// The timestamp when the delivery was completed (success or failure).
    /// </summary>
    public DateTime DeliveredAtUtc { get; }

    private DeliveryResult(bool success, string? providerMessageId, string? failureReason, DateTime deliveredAtUtc)
    {
        Success = success;
        ProviderMessageId = providerMessageId;
        FailureReason = failureReason;
        DeliveredAtUtc = deliveredAtUtc;
    }

    /// <summary>
    /// Creates a successful delivery result.
    /// </summary>
    public static DeliveryResult Succeeded(string providerMessageId)
    {
        if (string.IsNullOrWhiteSpace(providerMessageId))
            throw new ArgumentException("Provider message ID is required for successful delivery.", nameof(providerMessageId));

        return new DeliveryResult(
            success: true,
            providerMessageId: providerMessageId,
            failureReason: null,
            deliveredAtUtc: DateTime.UtcNow);
    }

    /// <summary>
    /// Creates a failed delivery result.
    /// </summary>
    public static DeliveryResult Failed(string failureReason)
    {
        if (string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("Failure reason is required for failed delivery.", nameof(failureReason));

        return new DeliveryResult(
            success: false,
            providerMessageId: null,
            failureReason: failureReason,
            deliveredAtUtc: DateTime.UtcNow);
    }

    /// <summary>
    /// Creates a failed delivery result that still has a provider message ID
    /// (e.g., when provider accepted but later reported failure).
    /// </summary>
    public static DeliveryResult FailedWithMessageId(string providerMessageId, string failureReason)
    {
        if (string.IsNullOrWhiteSpace(providerMessageId))
            throw new ArgumentException("Provider message ID is required.", nameof(providerMessageId));

        if (string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("Failure reason is required for failed delivery.", nameof(failureReason));

        return new DeliveryResult(
            success: false,
            providerMessageId: providerMessageId,
            failureReason: failureReason,
            deliveredAtUtc: DateTime.UtcNow);
    }
}
