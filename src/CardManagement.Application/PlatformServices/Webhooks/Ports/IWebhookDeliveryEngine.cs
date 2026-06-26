using CardManagement.Domain.PlatformServices.Webhooks;

namespace CardManagement.Application.PlatformServices.Webhooks.Ports;

/// <summary>
/// Port for the webhook delivery engine responsible for HTTP dispatch with HMAC signing.
/// </summary>
public interface IWebhookDeliveryEngine
{
    Task DeliverAsync(WebhookDelivery delivery, CancellationToken ct);
    Task RetryAsync(Guid deliveryId, CancellationToken ct);
    Task ReplayFromDlqAsync(Guid dlqItemId, CancellationToken ct);
}
