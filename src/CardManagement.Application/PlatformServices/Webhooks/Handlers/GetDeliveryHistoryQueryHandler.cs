using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Application.PlatformServices.Webhooks.Queries;
using CardManagement.Domain.PlatformServices.Webhooks;

namespace CardManagement.Application.PlatformServices.Webhooks.Handlers;

/// <summary>
/// Handles queries for webhook delivery history for a given subscription.
/// Exposes delivery attempt history (timestamp, HTTP status, response time) per requirement 6.4.
/// </summary>
public class GetDeliveryHistoryQueryHandler
{
    private readonly IWebhookDeliveryRepository _deliveryRepository;

    public GetDeliveryHistoryQueryHandler(IWebhookDeliveryRepository deliveryRepository)
    {
        _deliveryRepository = deliveryRepository;
    }

    public async Task<IReadOnlyList<WebhookDelivery>> HandleAsync(GetDeliveryHistoryQuery query, CancellationToken ct)
    {
        return await _deliveryRepository.GetBySubscriptionAsync(
            query.SubscriptionId,
            query.Limit,
            query.Offset,
            ct);
    }
}
