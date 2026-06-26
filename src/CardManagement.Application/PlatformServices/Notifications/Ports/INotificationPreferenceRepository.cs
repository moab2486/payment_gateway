using CardManagement.Domain.PlatformServices.Notifications;

namespace CardManagement.Application.PlatformServices.Notifications.Ports;

/// <summary>
/// Repository for accessing and updating notification delivery preferences per recipient.
/// </summary>
public interface INotificationPreferenceRepository
{
    Task<NotificationPreference?> GetByRecipientAsync(string recipientId, CancellationToken ct);
    Task SaveAsync(NotificationPreference preference, CancellationToken ct);
}
