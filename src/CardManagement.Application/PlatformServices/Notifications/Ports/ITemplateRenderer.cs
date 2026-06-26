using CardManagement.Domain.PlatformServices.Notifications;

namespace CardManagement.Application.PlatformServices.Notifications.Ports;

/// <summary>
/// Renders a notification template by substituting variable placeholders
/// with provided values for a specific delivery channel.
/// </summary>
public interface ITemplateRenderer
{
    RenderedNotification Render(
        NotificationTemplate template,
        NotificationChannel channel,
        string recipientId,
        string recipientAddress,
        Dictionary<string, string> variables);
}
