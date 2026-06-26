namespace CardManagement.Application.PlatformServices.Webhooks.Commands;

/// <summary>
/// Command to deactivate a webhook subscription. Stops all future deliveries.
/// </summary>
public record DeactivateSubscriptionCommand(
    Guid SubscriptionId
);
