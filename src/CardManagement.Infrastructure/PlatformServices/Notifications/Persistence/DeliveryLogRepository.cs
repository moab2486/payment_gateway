using CardManagement.Application.PlatformServices.Notifications.DTOs;
using CardManagement.Application.PlatformServices.Notifications.Ports;
using CardManagement.Domain.PlatformServices.Notifications;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.PlatformServices.Notifications.Persistence;

/// <summary>
/// EF Core implementation of IDeliveryLogRepository.
/// Provides CRUD operations and aggregation queries for notification delivery logs.
/// </summary>
public class DeliveryLogRepository : IDeliveryLogRepository
{
    private readonly CardManagementDbContext _dbContext;

    public DeliveryLogRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task CreateAsync(DeliveryLogEntry entry, CancellationToken ct)
    {
        _dbContext.NotificationDeliveryLogs.Add(entry);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task UpdateStatusAsync(Guid entryId, NotificationDeliveryStatus status, CancellationToken ct)
    {
        var entry = await _dbContext.NotificationDeliveryLogs.FindAsync(new object[] { entryId }, ct);
        if (entry is null)
            throw new InvalidOperationException($"Delivery log entry with ID {entryId} not found.");

        switch (status)
        {
            case NotificationDeliveryStatus.Delivered:
                entry.MarkDelivered();
                break;
            case NotificationDeliveryStatus.Failed:
                entry.MarkFailed("Status updated externally");
                break;
            case NotificationDeliveryStatus.Sent:
                entry.MarkSent("provider-update");
                break;
            default:
                throw new ArgumentException($"Cannot transition to status '{status}' via UpdateStatusAsync.", nameof(status));
        }

        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DeliveryLogEntry>> GetByRecipientAsync(string recipientId, int limit, CancellationToken ct)
    {
        return await _dbContext.NotificationDeliveryLogs
            .AsNoTracking()
            .Where(e => e.RecipientId == recipientId)
            .OrderByDescending(e => e.DispatchedAtUtc)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task<DeliveryStatistics> GetStatisticsAsync(
        NotificationChannel? channel,
        Guid? templateId,
        DateRange range,
        CancellationToken ct)
    {
        var query = _dbContext.NotificationDeliveryLogs
            .AsNoTracking()
            .Where(e => e.DispatchedAtUtc >= range.StartUtc && e.DispatchedAtUtc <= range.EndUtc);

        if (channel.HasValue)
        {
            query = query.Where(e => e.Channel == channel.Value);
        }

        if (templateId.HasValue)
        {
            query = query.Where(e => e.TemplateId == templateId.Value);
        }

        var entries = await query.ToListAsync(ct);

        if (entries.Count == 0)
        {
            return new DeliveryStatistics(0, 0, 0, 0, range.StartUtc, range.EndUtc);
        }

        var totalSent = entries.Count(e =>
            e.Status == NotificationDeliveryStatus.Sent ||
            e.Status == NotificationDeliveryStatus.Delivered ||
            e.Status == NotificationDeliveryStatus.Failed);

        var totalDelivered = entries.Count(e => e.Status == NotificationDeliveryStatus.Delivered);
        var totalFailed = entries.Count(e => e.Status == NotificationDeliveryStatus.Failed);

        var deliveredEntries = entries
            .Where(e => e.DeliveredAtUtc.HasValue)
            .ToList();

        var averageDeliveryTimeMs = deliveredEntries.Count > 0
            ? deliveredEntries.Average(e => (e.DeliveredAtUtc!.Value - e.DispatchedAtUtc).TotalMilliseconds)
            : 0;

        return new DeliveryStatistics(
            TotalSent: totalSent,
            TotalDelivered: totalDelivered,
            TotalFailed: totalFailed,
            AverageDeliveryTimeMs: averageDeliveryTimeMs,
            PeriodStartUtc: range.StartUtc,
            PeriodEndUtc: range.EndUtc);
    }
}
