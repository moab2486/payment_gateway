namespace CardManagement.Application.PlatformServices.Webhooks.Queries;

/// <summary>
/// Query to retrieve delivery history for a webhook subscription with pagination.
/// </summary>
public record GetDeliveryHistoryQuery(
    Guid SubscriptionId,
    int Limit = 20,
    int Offset = 0
);
