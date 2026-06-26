namespace CardManagement.Domain.PlatformServices.Webhooks;

/// <summary>
/// Represents the delivery status of a webhook event payload.
/// </summary>
public enum DeliveryStatus
{
    Pending,
    Delivered,
    Failed,
    DeadLettered
}
