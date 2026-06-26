using CardManagement.Domain.PlatformServices.Webhooks;

namespace CardManagement.Application.PlatformServices.Webhooks.Ports;

/// <summary>
/// Repository port for persisting and querying webhook deliveries and DLQ items.
/// </summary>
public interface IWebhookDeliveryRepository
{
    Task<WebhookDelivery> CreateAsync(WebhookDelivery delivery, CancellationToken ct);
    Task UpdateAsync(WebhookDelivery delivery, CancellationToken ct);
    Task<WebhookDelivery?> GetByIdAsync(Guid deliveryId, CancellationToken ct);
    Task<IReadOnlyList<WebhookDelivery>> GetBySubscriptionAsync(Guid subscriptionId, int limit, int offset, CancellationToken ct);
    Task MoveToDlqAsync(Guid deliveryId, CancellationToken ct);
    Task<IReadOnlyList<DlqItem>> GetDlqItemsAsync(Guid subscriptionId, CancellationToken ct);
    Task<DlqItem?> GetDlqItemByIdAsync(Guid dlqItemId, CancellationToken ct);
}
