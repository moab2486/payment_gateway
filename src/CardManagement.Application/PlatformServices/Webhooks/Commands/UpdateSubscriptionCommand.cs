namespace CardManagement.Application.PlatformServices.Webhooks.Commands;

/// <summary>
/// Command to update an existing webhook subscription's destination URL and/or event types.
/// </summary>
public record UpdateSubscriptionCommand(
    Guid SubscriptionId,
    string? DestinationUrl,
    string[]? EventTypes
);
