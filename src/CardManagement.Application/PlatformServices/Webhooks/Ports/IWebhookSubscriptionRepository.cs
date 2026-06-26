using CardManagement.Domain.PlatformServices.Webhooks;

namespace CardManagement.Application.PlatformServices.Webhooks.Ports;

/// <summary>
/// Repository port for persisting and querying webhook subscriptions.
/// </summary>
public interface IWebhookSubscriptionRepository
{
    Task<WebhookSubscription> CreateAsync(WebhookSubscription subscription, CancellationToken ct);
    Task<WebhookSubscription?> GetByIdAsync(Guid subscriptionId, CancellationToken ct);
    Task<IReadOnlyList<WebhookSubscription>> GetActiveByEventTypeAsync(string eventType, CancellationToken ct);
    Task<int> CountActiveByMerchantAsync(Guid merchantId, CancellationToken ct);
    Task<IReadOnlyList<WebhookSubscription>> GetByMerchantAsync(Guid merchantId, CancellationToken ct);
    Task UpdateAsync(WebhookSubscription subscription, CancellationToken ct);
}
