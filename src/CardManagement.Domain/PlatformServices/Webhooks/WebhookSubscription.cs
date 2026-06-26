namespace CardManagement.Domain.PlatformServices.Webhooks;

/// <summary>
/// Represents a merchant's webhook subscription linking event types to a destination URL.
/// Manages lifecycle transitions (Active → Suspended → Deactivated) and tracks delivery health
/// via consecutive failure counting.
/// </summary>
public class WebhookSubscription
{
    public Guid Id { get; private set; }
    public Guid MerchantId { get; private set; }
    public string DestinationUrl { get; private set; } = string.Empty;
    public string[] EventTypes { get; private set; } = Array.Empty<string>();
    public string SigningSecret { get; private set; } = string.Empty;
    public SubscriptionStatus Status { get; private set; }
    public int ConsecutiveFailures { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? LastDeliveryAtUtc { get; private set; }

    private WebhookSubscription() { }

    /// <summary>
    /// Creates a new active webhook subscription after URL verification has succeeded.
    /// </summary>
    public static WebhookSubscription Create(
        Guid merchantId,
        string destinationUrl,
        string[] eventTypes,
        string signingSecret)
    {
        if (merchantId == Guid.Empty)
            throw new ArgumentException("Merchant ID is required.", nameof(merchantId));

        if (string.IsNullOrWhiteSpace(destinationUrl))
            throw new ArgumentException("Destination URL is required.", nameof(destinationUrl));

        if (eventTypes is null || eventTypes.Length == 0)
            throw new ArgumentException("At least one event type is required.", nameof(eventTypes));

        if (string.IsNullOrWhiteSpace(signingSecret))
            throw new ArgumentException("Signing secret is required.", nameof(signingSecret));

        return new WebhookSubscription
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            DestinationUrl = destinationUrl,
            EventTypes = eventTypes,
            SigningSecret = signingSecret,
            Status = SubscriptionStatus.Active,
            ConsecutiveFailures = 0,
            CreatedAtUtc = TruncateToMilliseconds(DateTime.UtcNow)
        };
    }

    /// <summary>
    /// Updates the subscription's destination URL and/or event types.
    /// Only active or suspended subscriptions can be updated.
    /// </summary>
    public void Update(string? destinationUrl, string[]? eventTypes)
    {
        if (Status == SubscriptionStatus.Deactivated)
            throw new InvalidOperationException("Cannot update a deactivated subscription.");

        if (destinationUrl is not null)
        {
            if (string.IsNullOrWhiteSpace(destinationUrl))
                throw new ArgumentException("Destination URL cannot be empty.", nameof(destinationUrl));

            DestinationUrl = destinationUrl;
        }

        if (eventTypes is not null)
        {
            if (eventTypes.Length == 0)
                throw new ArgumentException("At least one event type is required.", nameof(eventTypes));

            EventTypes = eventTypes;
        }
    }

    /// <summary>
    /// Records a successful delivery, resetting the consecutive failure counter.
    /// </summary>
    public void RecordSuccessfulDelivery()
    {
        if (Status == SubscriptionStatus.Deactivated)
            throw new InvalidOperationException("Cannot record delivery for a deactivated subscription.");

        ConsecutiveFailures = 0;
        LastDeliveryAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    /// <summary>
    /// Records a failed delivery, incrementing the consecutive failure counter.
    /// </summary>
    public void RecordFailedDelivery()
    {
        if (Status == SubscriptionStatus.Deactivated)
            throw new InvalidOperationException("Cannot record delivery for a deactivated subscription.");

        ConsecutiveFailures++;
        LastDeliveryAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    /// <summary>
    /// Suspends the subscription when consecutive failures exceed the threshold.
    /// Only active subscriptions can be suspended.
    /// </summary>
    public void Suspend()
    {
        if (Status != SubscriptionStatus.Active)
            throw new InvalidOperationException("Can only suspend an active subscription.");

        Status = SubscriptionStatus.Suspended;
    }

    /// <summary>
    /// Reactivates a suspended subscription, resetting the failure counter.
    /// </summary>
    public void Reactivate()
    {
        if (Status != SubscriptionStatus.Suspended)
            throw new InvalidOperationException("Can only reactivate a suspended subscription.");

        Status = SubscriptionStatus.Active;
        ConsecutiveFailures = 0;
    }

    /// <summary>
    /// Permanently deactivates the subscription. No further deliveries will be attempted.
    /// </summary>
    public void Deactivate()
    {
        if (Status == SubscriptionStatus.Deactivated)
            throw new InvalidOperationException("Subscription is already deactivated.");

        Status = SubscriptionStatus.Deactivated;
    }

    /// <summary>
    /// Checks whether consecutive failures have reached or exceeded the given threshold.
    /// </summary>
    public bool HasExceededFailureThreshold(int threshold)
    {
        if (threshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(threshold), "Threshold must be positive.");

        return ConsecutiveFailures >= threshold;
    }

    private static DateTime TruncateToMilliseconds(DateTime dateTime)
    {
        return new DateTime(
            dateTime.Ticks - (dateTime.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
