namespace CardManagement.Application.PlatformServices.Notifications.Commands;

/// <summary>
/// Command to create a new notification template with channel-specific content.
/// </summary>
public record CreateTemplateCommand(
    string Name,
    string Category,
    string[] RequiredVariables,
    string? EmailSubjectTemplate,
    string? EmailBodyTemplate,
    string? SmsBodyTemplate,
    string? WhatsAppBodyTemplate);
