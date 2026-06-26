namespace CardManagement.Application.PlatformServices.Notifications.DTOs;

/// <summary>
/// Represents a request to dispatch a notification to a recipient.
/// Contains the template reference, recipient, and variable map for rendering.
/// </summary>
public record NotificationRequest(
    string RecipientId,
    string RecipientAddress,
    Guid TemplateId,
    Dictionary<string, string> Variables);
