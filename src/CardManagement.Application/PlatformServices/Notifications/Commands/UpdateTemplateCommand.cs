namespace CardManagement.Application.PlatformServices.Notifications.Commands;

/// <summary>
/// Command to update an existing notification template's content and variables.
/// </summary>
public record UpdateTemplateCommand(
    Guid TemplateId,
    string? EmailSubjectTemplate,
    string? EmailBodyTemplate,
    string? SmsBodyTemplate,
    string? WhatsAppBodyTemplate,
    string[] RequiredVariables);
