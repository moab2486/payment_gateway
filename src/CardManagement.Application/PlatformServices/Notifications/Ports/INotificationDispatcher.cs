using CardManagement.Application.PlatformServices.Notifications.DTOs;

namespace CardManagement.Application.PlatformServices.Notifications.Ports;

/// <summary>
/// Orchestrates notification dispatch: resolves preferences, renders templates,
/// sends via primary channel, and falls back on failure.
/// </summary>
public interface INotificationDispatcher
{
    Task DispatchAsync(NotificationRequest request, CancellationToken ct);
}
