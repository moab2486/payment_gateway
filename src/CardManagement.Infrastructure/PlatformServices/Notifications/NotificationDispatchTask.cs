namespace CardManagement.Infrastructure.PlatformServices.Notifications;

/// <summary>
/// Represents a notification dispatch task to be processed by the notification worker pool.
/// Contains all information needed to resolve preferences, render a template, and deliver.
/// </summary>
public record NotificationDispatchTask(
    string RecipientId,
    string RecipientAddress,
    Guid TemplateId,
    Dictionary<string, string> Variables);
