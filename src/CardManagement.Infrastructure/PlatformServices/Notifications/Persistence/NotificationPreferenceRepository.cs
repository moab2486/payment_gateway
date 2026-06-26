using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Domain.PlatformServices.Notifications;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.PlatformServices.Notifications.Persistence;

/// <summary>
/// EF Core implementation of INotificationPreferenceRepository.
/// Provides access to notification delivery preferences per recipient.
/// </summary>
public class NotificationPreferenceRepository : INotificationPreferenceRepository
{
    private readonly CardManagementDbContext _dbContext;

    public NotificationPreferenceRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<NotificationPreference?> GetByRecipientAsync(string recipientId, CancellationToken ct)
    {
        return await _dbContext.NotificationPreferences
            .FirstOrDefaultAsync(p => p.RecipientId == recipientId, ct);
    }

    public async Task SaveAsync(NotificationPreference preference, CancellationToken ct)
    {
        var existing = await _dbContext.NotificationPreferences
            .FirstOrDefaultAsync(p => p.RecipientId == preference.RecipientId, ct);

        if (existing is null)
            _dbContext.NotificationPreferences.Add(preference);
        else
            _dbContext.NotificationPreferences.Update(preference);

        await _dbContext.SaveChangesAsync(ct);
    }
}
