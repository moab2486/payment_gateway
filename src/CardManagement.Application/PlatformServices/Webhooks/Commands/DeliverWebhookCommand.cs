namespace CardManagement.Application.PlatformServices.Webhooks.Commands;

/// <summary>
/// Command to deliver a webhook event to all active subscriptions matching the event type.
/// Constructs the payload, signs with HMAC, and dispatches via the delivery engine.
/// </summary>
public record DeliverWebhookCommand(
    string EventType,
    string EventData,
    DateTime TimestampUtc
);
