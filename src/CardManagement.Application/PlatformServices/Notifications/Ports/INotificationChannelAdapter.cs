using CardManagement.Domain.PlatformServices.Notifications;

namespace CardManagement.Application.PlatformServices.Notifications.Ports;

/// <summary>
/// Channel-specific adapter for sending rendered notifications.
/// Implementations exist for Email (SMTP), SMS, and WhatsApp (WAHA).
/// </summary>
public interface INotificationChannelAdapter
{
    NotificationChannel Channel { get; }
    Task<DeliveryResult> SendAsync(RenderedNotification notification, CancellationToken ct);
}
