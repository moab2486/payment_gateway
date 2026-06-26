namespace CardManagement.Domain.PlatformServices.Webhooks;

/// <summary>
/// Represents a webhook delivery that has been moved to the dead-letter queue
/// after exhausting all retry attempts. Preserves the original payload for replay.
/// </summary>
public class DlqItem
{
    public Guid Id { get; private set; }
    public Guid DeliveryId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public string OriginalPayload { get; private set; } = string.Empty;
    public string LastError { get; private set; } = string.Empty;
    public DateTime MovedToDlqAtUtc { get; private set; }
    public bool Replayed { get; private set; }

    private DlqItem() { }

    /// <summary>
    /// Creates a new dead-letter queue item from a failed delivery.
    /// </summary>
    public static DlqItem Create(
        Guid deliveryId,
        Guid subscriptionId,
        string originalPayload,
        string lastError)
    {
        if (deliveryId == Guid.Empty)
            throw new ArgumentException("Delivery ID is required.", nameof(deliveryId));

        if (subscriptionId == Guid.Empty)
            throw new ArgumentException("Subscription ID is required.", nameof(subscriptionId));

        if (string.IsNullOrWhiteSpace(originalPayload))
            throw new ArgumentException("Original payload is required.", nameof(originalPayload));

        if (string.IsNullOrWhiteSpace(lastError))
            throw new ArgumentException("Last error is required.", nameof(lastError));

        return new DlqItem
        {
            Id = Guid.NewGuid(),
            DeliveryId = deliveryId,
            SubscriptionId = subscriptionId,
            OriginalPayload = originalPayload,
            LastError = lastError,
            MovedToDlqAtUtc = TruncateToMilliseconds(DateTime.UtcNow),
            Replayed = false
        };
    }

    /// <summary>
    /// Marks this DLQ item as replayed. Once replayed, it cannot be replayed again.
    /// </summary>
    public void MarkReplayed()
    {
        if (Replayed)
            throw new InvalidOperationException("This DLQ item has already been replayed.");

        Replayed = true;
    }

    private static DateTime TruncateToMilliseconds(DateTime dateTime)
    {
        return new DateTime(
            dateTime.Ticks - (dateTime.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
