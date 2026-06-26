namespace CardManagement.Domain.PlatformServices.Webhooks;

/// <summary>
/// Represents a single webhook delivery for a subscription. Tracks the delivery lifecycle
/// from creation through delivery or dead-lettering, including all intermediate retry attempts.
/// </summary>
public class WebhookDelivery
{
    public Guid Id { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DeliveryStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? NextRetryAtUtc { get; private set; }
    public List<DeliveryAttempt> Attempts { get; private set; } = new();

    private WebhookDelivery() { }

    /// <summary>
    /// Creates a new pending webhook delivery for a subscription.
    /// </summary>
    public static WebhookDelivery Create(
        Guid subscriptionId,
        string eventType,
        string payload)
    {
        if (subscriptionId == Guid.Empty)
            throw new ArgumentException("Subscription ID is required.", nameof(subscriptionId));

        if (string.IsNullOrWhiteSpace(eventType))
            throw new ArgumentException("Event type is required.", nameof(eventType));

        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentException("Payload is required.", nameof(payload));

        return new WebhookDelivery
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            EventType = eventType,
            Payload = payload,
            Status = DeliveryStatus.Pending,
            AttemptCount = 0,
            CreatedAtUtc = TruncateToMilliseconds(DateTime.UtcNow)
        };
    }

    /// <summary>
    /// Records a successful delivery attempt and marks the delivery as delivered.
    /// </summary>
    public void MarkDelivered(int httpStatusCode, TimeSpan responseTime)
    {
        if (Status is DeliveryStatus.Delivered or DeliveryStatus.DeadLettered)
            throw new InvalidOperationException($"Cannot mark as delivered when status is {Status}.");

        var attempt = new DeliveryAttempt(
            AttemptedAtUtc: TruncateToMilliseconds(DateTime.UtcNow),
            HttpStatusCode: httpStatusCode,
            ResponseTime: responseTime,
            ErrorMessage: null);

        Attempts.Add(attempt);
        AttemptCount++;
        Status = DeliveryStatus.Delivered;
        NextRetryAtUtc = null;
    }

    /// <summary>
    /// Records a failed delivery attempt. Does not change overall status — caller should
    /// invoke ScheduleRetry or MoveToDlq based on retry policy.
    /// </summary>
    public void RecordFailedAttempt(int httpStatusCode, TimeSpan responseTime, string? errorMessage)
    {
        if (Status is DeliveryStatus.Delivered or DeliveryStatus.DeadLettered)
            throw new InvalidOperationException($"Cannot record attempt when status is {Status}.");

        var attempt = new DeliveryAttempt(
            AttemptedAtUtc: TruncateToMilliseconds(DateTime.UtcNow),
            HttpStatusCode: httpStatusCode,
            ResponseTime: responseTime,
            ErrorMessage: errorMessage);

        Attempts.Add(attempt);
        AttemptCount++;
        Status = DeliveryStatus.Failed;
    }

    /// <summary>
    /// Schedules a retry attempt at the specified time using exponential backoff.
    /// </summary>
    public void ScheduleRetry(DateTime nextRetryAtUtc)
    {
        if (Status != DeliveryStatus.Failed)
            throw new InvalidOperationException("Can only schedule retry for a failed delivery.");

        NextRetryAtUtc = TruncateToMilliseconds(nextRetryAtUtc);
        Status = DeliveryStatus.Pending;
    }

    /// <summary>
    /// Moves the delivery to the dead-letter queue after all retries are exhausted.
    /// </summary>
    public void MoveToDlq()
    {
        if (Status != DeliveryStatus.Failed)
            throw new InvalidOperationException("Can only move to DLQ from a failed status.");

        Status = DeliveryStatus.DeadLettered;
        NextRetryAtUtc = null;
    }

    /// <summary>
    /// Resets the delivery for a DLQ replay attempt, making it pending again.
    /// </summary>
    public void ResetForReplay()
    {
        if (Status != DeliveryStatus.DeadLettered)
            throw new InvalidOperationException("Can only replay a dead-lettered delivery.");

        Status = DeliveryStatus.Pending;
        NextRetryAtUtc = null;
    }

    private static DateTime TruncateToMilliseconds(DateTime dateTime)
    {
        return new DateTime(
            dateTime.Ticks - (dateTime.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
