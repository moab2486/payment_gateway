namespace CardManagement.Domain.PlatformServices.Notifications;

/// <summary>
/// Tracks the delivery status of a notification dispatch attempt.
/// Records the recipient, channel, template, timestamps, and outcome.
/// </summary>
public class DeliveryLogEntry
{
    public Guid Id { get; private set; }
    public string RecipientId { get; private set; } = string.Empty;
    public NotificationChannel Channel { get; private set; }
    public Guid TemplateId { get; private set; }
    public NotificationDeliveryStatus Status { get; private set; }
    public DateTime DispatchedAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }
    public string? FailureReason { get; private set; }
    public string? ProviderMessageId { get; private set; }

    private DeliveryLogEntry() { }

    public static DeliveryLogEntry Create(
        string recipientId,
        NotificationChannel channel,
        Guid templateId)
    {
        if (string.IsNullOrWhiteSpace(recipientId))
            throw new ArgumentException("Recipient ID is required.", nameof(recipientId));

        if (templateId == Guid.Empty)
            throw new ArgumentException("Template ID is required.", nameof(templateId));

        return new DeliveryLogEntry
        {
            Id = Guid.NewGuid(),
            RecipientId = recipientId,
            Channel = channel,
            TemplateId = templateId,
            Status = NotificationDeliveryStatus.Pending,
            DispatchedAtUtc = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Marks the notification as sent by the provider.
    /// </summary>
    public void MarkSent(string providerMessageId)
    {
        if (Status != NotificationDeliveryStatus.Pending)
            throw new InvalidOperationException($"Cannot mark as sent from status '{Status}'.");

        if (string.IsNullOrWhiteSpace(providerMessageId))
            throw new ArgumentException("Provider message ID is required.", nameof(providerMessageId));

        Status = NotificationDeliveryStatus.Sent;
        ProviderMessageId = providerMessageId;
    }

    /// <summary>
    /// Marks the notification as delivered to the recipient.
    /// </summary>
    public void MarkDelivered()
    {
        if (Status is not (NotificationDeliveryStatus.Pending or NotificationDeliveryStatus.Sent))
            throw new InvalidOperationException($"Cannot mark as delivered from status '{Status}'.");

        Status = NotificationDeliveryStatus.Delivered;
        DeliveredAtUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Marks the notification delivery as failed.
    /// </summary>
    public void MarkFailed(string failureReason)
    {
        if (Status is not (NotificationDeliveryStatus.Pending or NotificationDeliveryStatus.Sent))
            throw new InvalidOperationException($"Cannot mark as failed from status '{Status}'.");

        if (string.IsNullOrWhiteSpace(failureReason))
            throw new ArgumentException("Failure reason is required.", nameof(failureReason));

        Status = NotificationDeliveryStatus.Failed;
        FailureReason = failureReason;
    }
}
