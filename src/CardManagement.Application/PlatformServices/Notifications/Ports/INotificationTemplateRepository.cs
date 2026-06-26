using CardManagement.Domain.PlatformServices.Notifications;

namespace CardManagement.Application.PlatformServices.Notifications.Ports;

/// <summary>
/// Repository for CRUD operations on notification templates.
/// </summary>
public interface INotificationTemplateRepository
{
    Task<NotificationTemplate?> GetByIdAsync(Guid templateId, CancellationToken ct);
    Task<NotificationTemplate?> GetByNameAsync(string templateName, CancellationToken ct);
    Task<IReadOnlyList<NotificationTemplate>> ListAsync(int limit, int offset, CancellationToken ct);
    Task CreateAsync(NotificationTemplate template, CancellationToken ct);
    Task UpdateAsync(NotificationTemplate template, CancellationToken ct);
}
