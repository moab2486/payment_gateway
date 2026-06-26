namespace CardManagement.Application.PlatformServices.Webhooks.Commands;

/// <summary>
/// Command to create a new webhook subscription for a merchant.
/// Triggers URL verification challenge before persisting.
/// </summary>
public record CreateSubscriptionCommand(
    Guid MerchantId,
    string DestinationUrl,
    string[] EventTypes,
    string SigningSecret
);
