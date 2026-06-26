using CardManagement.Application.PlatformServices.Notifications.DTOs;
using CardManagement.Domain.PlatformServices.Notifications;

namespace CardManagement.Application.PlatformServices.Notifications.Ports;

/// <summary>
/// Repository for creating and querying notification delivery log entries.
/// </summary>
public interface IDeliveryLogRepository
{
    Task CreateAsync(DeliveryLogEntry entry, CancellationToken ct);
    Task UpdateStatusAsync(Guid entryId, NotificationDeliveryStatus status, CancellationToken ct);
    Task<IReadOnlyList<DeliveryLogEntry>> GetByRecipientAsync(string recipientId, int limit, CancellationToken ct);
    Task<DeliveryStatistics> GetStatisticsAsync(NotificationChannel? channel, Guid? templateId, DateRange range, CancellationToken ct);
}
