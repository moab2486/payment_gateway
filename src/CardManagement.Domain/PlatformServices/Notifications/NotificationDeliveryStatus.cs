namespace CardManagement.Domain.PlatformServices.Notifications;

/// <summary>
/// Represents the delivery status of a notification dispatch attempt.
/// Distinct from Webhooks.DeliveryStatus as notification delivery follows
/// a different lifecycle (Pending → Sent → Delivered vs Pending → Delivered).
/// </summary>
public enum NotificationDeliveryStatus
{
    Pending,
    Sent,
    Delivered,
    Failed
}
