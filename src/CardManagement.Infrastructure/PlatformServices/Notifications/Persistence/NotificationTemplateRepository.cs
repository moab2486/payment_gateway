using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Domain.PlatformServices.Notifications;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.PlatformServices.Notifications.Persistence;

/// <summary>
/// EF Core implementation of INotificationTemplateRepository.
/// Provides CRUD operations for notification templates.
/// </summary>
public class NotificationTemplateRepository : INotificationTemplateRepository
{
    private readonly CardManagementDbContext _dbContext;

    public NotificationTemplateRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<NotificationTemplate?> GetByIdAsync(Guid templateId, CancellationToken ct)
    {
        return await _dbContext.NotificationTemplates.FindAsync(new object[] { templateId }, ct);
    }

    public async Task<NotificationTemplate?> GetByNameAsync(string templateName, CancellationToken ct)
    {
        return await _dbContext.NotificationTemplates
            .FirstOrDefaultAsync(t => t.Name == templateName, ct);
    }

    public async Task CreateAsync(NotificationTemplate template, CancellationToken ct)
    {
        _dbContext.NotificationTemplates.Add(template);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NotificationTemplate>> ListAsync(int limit, int offset, CancellationToken ct)
    {
        return await _dbContext.NotificationTemplates
            .OrderBy(t => t.Name)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task UpdateAsync(NotificationTemplate template, CancellationToken ct)
    {
        _dbContext.NotificationTemplates.Update(template);
        await _dbContext.SaveChangesAsync(ct);
    }
}
