using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Domain.PlatformServices.Notifications;

namespace CardManagement.Application.PlatformServices.Notifications.Commands;

/// <summary>
/// Handles creation of a new notification template.
/// Validates uniqueness by name before persisting.
/// </summary>
public class CreateTemplateCommandHandler
{
    private readonly INotificationTemplateRepository _templateRepository;

    public CreateTemplateCommandHandler(INotificationTemplateRepository templateRepository)
    {
        _templateRepository = templateRepository ?? throw new ArgumentNullException(nameof(templateRepository));
    }

    public async Task<Guid> HandleAsync(CreateTemplateCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Name))
            throw new ArgumentException("Template name is required.", nameof(command));

        if (string.IsNullOrWhiteSpace(command.Category))
            throw new ArgumentException("Template category is required.", nameof(command));

        // Ensure template name is unique
        var existing = await _templateRepository.GetByNameAsync(command.Name, ct);
        if (existing is not null)
            throw new InvalidOperationException($"A template with name '{command.Name}' already exists.");

        var template = NotificationTemplate.Create(
            name: command.Name,
            category: command.Category,
            requiredVariables: command.RequiredVariables,
            emailSubjectTemplate: command.EmailSubjectTemplate,
            emailBodyTemplate: command.EmailBodyTemplate,
            smsBodyTemplate: command.SmsBodyTemplate,
            whatsAppBodyTemplate: command.WhatsAppBodyTemplate);

        await _templateRepository.CreateAsync(template, ct);

        return template.Id;
    }
}
