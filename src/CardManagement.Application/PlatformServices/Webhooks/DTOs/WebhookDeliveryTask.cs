namespace CardManagement.Application.PlatformServices.Webhooks.DTOs;

/// <summary>
/// Represents a webhook delivery task to be processed by the worker pool.
/// Contains the delivery ID and whether this is a retry attempt.
/// </summary>
public record WebhookDeliveryTask(
    Guid DeliveryId,
    bool IsRetry = false);
