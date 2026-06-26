using CardManagement.Application.PlatformServices.Notifications.Ports;

namespace CardManagement.Application.PlatformServices.Notifications.Commands;

/// <summary>
/// Handles updates to an existing notification template.
/// Loads the template, applies the update, and persists changes.
/// </summary>
public class UpdateTemplateCommandHandler
{
    private readonly INotificationTemplateRepository _templateRepository;

    public UpdateTemplateCommandHandler(INotificationTemplateRepository templateRepository)
    {
        _templateRepository = templateRepository ?? throw new ArgumentNullException(nameof(templateRepository));
    }

    public async Task HandleAsync(UpdateTemplateCommand command, CancellationToken ct)
    {
        if (command.TemplateId == Guid.Empty)
            throw new ArgumentException("Template ID is required.", nameof(command));

        var template = await _templateRepository.GetByIdAsync(command.TemplateId, ct)
            ?? throw new InvalidOperationException($"Template '{command.TemplateId}' not found.");

        template.Update(
            emailSubjectTemplate: command.EmailSubjectTemplate,
            emailBodyTemplate: command.EmailBodyTemplate,
            smsBodyTemplate: command.SmsBodyTemplate,
            whatsAppBodyTemplate: command.WhatsAppBodyTemplate,
            requiredVariables: command.RequiredVariables);

        await _templateRepository.UpdateAsync(template, ct);
    }
}
