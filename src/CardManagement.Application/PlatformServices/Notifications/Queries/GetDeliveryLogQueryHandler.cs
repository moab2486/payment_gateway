using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Domain.PlatformServices.Notifications;

namespace CardManagement.Application.PlatformServices.Notifications.Queries;

/// <summary>
/// Handles retrieval of delivery log entries for a recipient.
/// </summary>
public class GetDeliveryLogQueryHandler
{
    private readonly IDeliveryLogRepository _deliveryLogRepository;

    public GetDeliveryLogQueryHandler(IDeliveryLogRepository deliveryLogRepository)
    {
        _deliveryLogRepository = deliveryLogRepository ?? throw new ArgumentNullException(nameof(deliveryLogRepository));
    }

    public async Task<IReadOnlyList<DeliveryLogEntry>> HandleAsync(GetDeliveryLogQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.RecipientId))
            throw new ArgumentException("Recipient ID is required.", nameof(query));

        if (query.Limit <= 0)
            throw new ArgumentException("Limit must be greater than zero.", nameof(query));

        return await _deliveryLogRepository.GetByRecipientAsync(query.RecipientId, query.Limit, ct);
    }
}
