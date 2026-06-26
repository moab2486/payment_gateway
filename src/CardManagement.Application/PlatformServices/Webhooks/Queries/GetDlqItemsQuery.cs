namespace CardManagement.Application.PlatformServices.Webhooks.Queries;

/// <summary>
/// Query to retrieve dead-letter queue items for a webhook subscription.
/// </summary>
public record GetDlqItemsQuery(
    Guid SubscriptionId
);
