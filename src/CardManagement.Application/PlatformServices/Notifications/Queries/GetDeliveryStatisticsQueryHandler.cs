using CardManagement.Application.PlatformServices.Notifications.DTOs;
using CardManagement.Application.PlatformServices.Notifications.Ports;

namespace CardManagement.Application.PlatformServices.Notifications.Queries;

/// <summary>
/// Handles retrieval of aggregated delivery statistics per channel and template.
/// </summary>
public class GetDeliveryStatisticsQueryHandler
{
    private readonly IDeliveryLogRepository _deliveryLogRepository;

    public GetDeliveryStatisticsQueryHandler(IDeliveryLogRepository deliveryLogRepository)
    {
        _deliveryLogRepository = deliveryLogRepository ?? throw new ArgumentNullException(nameof(deliveryLogRepository));
    }

    public async Task<DeliveryStatistics> HandleAsync(GetDeliveryStatisticsQuery query, CancellationToken ct)
    {
        if (query.Range is null)
            throw new ArgumentException("Date range is required.", nameof(query));

        return await _deliveryLogRepository.GetStatisticsAsync(
            query.Channel, query.TemplateId, query.Range, ct);
    }
}
