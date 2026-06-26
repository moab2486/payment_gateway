using CardManagement.Application.PlatformServices.Notifications.DTOs;
using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Domain.PlatformServices.Notifications;

namespace CardManagement.Application.PlatformServices.Notifications.Commands;

/// <summary>
/// Handles notification dispatch: resolves recipient preference, renders template,
/// sends via primary channel, and attempts fallback channel on failure.
/// </summary>
public class DispatchNotificationCommandHandler
{
    private readonly INotificationPreferenceRepository _preferenceRepository;
    private readonly INotificationTemplateRepository _templateRepository;
    private readonly ITemplateRenderer _templateRenderer;
    private readonly IDeliveryLogRepository _deliveryLogRepository;
    private readonly IEnumerable<INotificationChannelAdapter> _channelAdapters;

    public DispatchNotificationCommandHandler(
        INotificationPreferenceRepository preferenceRepository,
        INotificationTemplateRepository templateRepository,
        ITemplateRenderer templateRenderer,
        IDeliveryLogRepository deliveryLogRepository,
        IEnumerable<INotificationChannelAdapter> channelAdapters)
    {
        _preferenceRepository = preferenceRepository ?? throw new ArgumentNullException(nameof(preferenceRepository));
        _templateRepository = templateRepository ?? throw new ArgumentNullException(nameof(templateRepository));
        _templateRenderer = templateRenderer ?? throw new ArgumentNullException(nameof(templateRenderer));
        _deliveryLogRepository = deliveryLogRepository ?? throw new ArgumentNullException(nameof(deliveryLogRepository));
        _channelAdapters = channelAdapters ?? throw new ArgumentNullException(nameof(channelAdapters));
    }

    public async Task HandleAsync(DispatchNotificationCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.RecipientId))
            throw new ArgumentException("Recipient ID is required.", nameof(command));

        if (command.TemplateId == Guid.Empty)
            throw new ArgumentException("Template ID is required.", nameof(command));

        // 1. Load the template
        var template = await _templateRepository.GetByIdAsync(command.TemplateId, ct)
            ?? throw new InvalidOperationException($"Template '{command.TemplateId}' not found.");

        // 2. Validate required variables are provided
        var missingVariables = template.RequiredVariables
            .Where(v => !command.Variables.ContainsKey(v))
            .ToArray();

        if (missingVariables.Length > 0)
            throw new InvalidOperationException(
                $"Missing required template variables: {string.Join(", ", missingVariables)}");

        // 3. Resolve recipient's notification preference
        var preference = await _preferenceRepository.GetByRecipientAsync(command.RecipientId, ct);
        var primaryChannel = preference?.PrimaryChannel ?? NotificationChannel.Email;
        var fallbackChannel = preference?.FallbackChannel;

        // 4. Check category opt-in
        if (preference is not null && !preference.IsOptedIn(template.Category))
            return; // Recipient opted out of this notification category

        // 5. Render the template for the primary channel
        if (!template.SupportsChannel(primaryChannel))
        {
            // If primary channel isn't supported by the template, try fallback
            if (fallbackChannel.HasValue && template.SupportsChannel(fallbackChannel.Value))
            {
                primaryChannel = fallbackChannel.Value;
                fallbackChannel = null;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Template '{template.Name}' does not support channel '{primaryChannel}'" +
                    (fallbackChannel.HasValue ? $" or fallback channel '{fallbackChannel.Value}'" : "") + ".");
            }
        }

        var rendered = _templateRenderer.Render(
            template, primaryChannel, command.RecipientId, command.RecipientAddress, command.Variables);

        // 6. Attempt delivery via primary channel
        var deliveryResult = await SendViaChannelAsync(rendered, primaryChannel, ct);

        if (deliveryResult.Success)
        {
            // Log successful delivery
            var logEntry = DeliveryLogEntry.Create(command.RecipientId, primaryChannel, command.TemplateId);
            logEntry.MarkSent(deliveryResult.ProviderMessageId!);
            logEntry.MarkDelivered();
            await _deliveryLogRepository.CreateAsync(logEntry, ct);
            return;
        }

        // 7. Primary channel failed — attempt fallback if configured
        if (fallbackChannel.HasValue && template.SupportsChannel(fallbackChannel.Value))
        {
            var fallbackRendered = _templateRenderer.Render(
                template, fallbackChannel.Value, command.RecipientId, command.RecipientAddress, command.Variables);

            var fallbackResult = await SendViaChannelAsync(fallbackRendered, fallbackChannel.Value, ct);

            if (fallbackResult.Success)
            {
                // Log successful fallback delivery
                var logEntry = DeliveryLogEntry.Create(command.RecipientId, fallbackChannel.Value, command.TemplateId);
                logEntry.MarkSent(fallbackResult.ProviderMessageId!);
                logEntry.MarkDelivered();
                await _deliveryLogRepository.CreateAsync(logEntry, ct);
                return;
            }

            // Both channels failed — log the failure
            var failedLog = DeliveryLogEntry.Create(command.RecipientId, fallbackChannel.Value, command.TemplateId);
            failedLog.MarkFailed(fallbackResult.FailureReason ?? "Delivery failed on fallback channel.");
            await _deliveryLogRepository.CreateAsync(failedLog, ct);
            return;
        }

        // No fallback available — log failure on primary channel
        var primaryFailedLog = DeliveryLogEntry.Create(command.RecipientId, primaryChannel, command.TemplateId);
        primaryFailedLog.MarkFailed(deliveryResult.FailureReason ?? "Delivery failed on primary channel.");
        await _deliveryLogRepository.CreateAsync(primaryFailedLog, ct);
    }

    private async Task<DeliveryResult> SendViaChannelAsync(
        RenderedNotification notification, NotificationChannel channel, CancellationToken ct)
    {
        var adapter = _channelAdapters.FirstOrDefault(a => a.Channel == channel);
        if (adapter is null)
            return DeliveryResult.Failed($"No adapter registered for channel '{channel}'.");

        return await adapter.SendAsync(notification, ct);
    }
}
