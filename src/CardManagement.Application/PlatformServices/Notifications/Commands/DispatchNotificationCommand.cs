namespace CardManagement.Application.PlatformServices.Notifications.Commands;

/// <summary>
/// Command to dispatch a notification to a recipient using their preferred channel.
/// </summary>
public record DispatchNotificationCommand(
    string RecipientId,
    string RecipientAddress,
    Guid TemplateId,
    Dictionary<string, string> Variables);
