using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Domain.PlatformServices.Notifications;

namespace CardManagement.Application.PlatformServices.Notifications.Queries;

/// <summary>
/// Handles retrieval of notification preferences for a recipient.
/// </summary>
public class GetPreferencesQueryHandler
{
    private readonly INotificationPreferenceRepository _preferenceRepository;

    public GetPreferencesQueryHandler(INotificationPreferenceRepository preferenceRepository)
    {
        _preferenceRepository = preferenceRepository ?? throw new ArgumentNullException(nameof(preferenceRepository));
    }

    public async Task<NotificationPreference?> HandleAsync(GetPreferencesQuery query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query.RecipientId))
            throw new ArgumentException("Recipient ID is required.", nameof(query));

        return await _preferenceRepository.GetByRecipientAsync(query.RecipientId, ct);
    }
}
