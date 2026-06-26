using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Application.PlatformServices.Webhooks.Queries;
using CardManagement.Domain.PlatformServices.Webhooks;

namespace CardManagement.Application.PlatformServices.Webhooks.Handlers;

/// <summary>
/// Handles queries for dead-letter queue items for a given subscription.
/// </summary>
public class GetDlqItemsQueryHandler
{
    private readonly IWebhookDeliveryRepository _deliveryRepository;

    public GetDlqItemsQueryHandler(IWebhookDeliveryRepository deliveryRepository)
    {
        _deliveryRepository = deliveryRepository;
    }

    public async Task<IReadOnlyList<DlqItem>> HandleAsync(GetDlqItemsQuery query, CancellationToken ct)
    {
        return await _deliveryRepository.GetDlqItemsAsync(query.SubscriptionId, ct);
    }
}
