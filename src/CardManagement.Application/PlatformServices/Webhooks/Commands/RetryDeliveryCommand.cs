namespace CardManagement.Application.PlatformServices.Webhooks.Commands;

/// <summary>
/// Command to retry a failed webhook delivery.
/// </summary>
public record RetryDeliveryCommand(
    Guid DeliveryId
);
