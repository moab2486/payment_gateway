using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Domain.PlatformServices.Webhooks;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.PlatformServices.Webhooks.Persistence;

/// <summary>
/// EF Core implementation of IWebhookSubscriptionRepository.
/// Provides CRUD operations, active count by merchant, and event type filtering.
/// </summary>
public class WebhookSubscriptionRepository : IWebhookSubscriptionRepository
{
    private readonly CardManagementDbContext _dbContext;

    public WebhookSubscriptionRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<WebhookSubscription> CreateAsync(WebhookSubscription subscription, CancellationToken ct)
    {
        _dbContext.WebhookSubscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync(ct);
        return subscription;
    }

    public async Task<WebhookSubscription?> GetByIdAsync(Guid subscriptionId, CancellationToken ct)
    {
        return await _dbContext.WebhookSubscriptions.FindAsync(new object[] { subscriptionId }, ct);
    }

    public async Task<IReadOnlyList<WebhookSubscription>> GetActiveByEventTypeAsync(string eventType, CancellationToken ct)
    {
        // Query subscriptions that are active and contain the event type in their EventTypes array.
        // PostgreSQL jsonb contains check via EF.Functions or raw SQL for array containment.
        var activeSubscriptions = await _dbContext.WebhookSubscriptions
            .Where(s => s.Status == SubscriptionStatus.Active)
            .ToListAsync(ct);

        // Filter in memory for event type array containment since jsonb array queries
        // require specialized handling that varies by provider
        return activeSubscriptions
            .Where(s => s.EventTypes.Contains(eventType))
            .ToList()
            .AsReadOnly();
    }

    public async Task<int> CountActiveByMerchantAsync(Guid merchantId, CancellationToken ct)
    {
        return await _dbContext.WebhookSubscriptions
            .CountAsync(s => s.MerchantId == merchantId && s.Status == SubscriptionStatus.Active, ct);
    }

    public async Task<IReadOnlyList<WebhookSubscription>> GetByMerchantAsync(Guid merchantId, CancellationToken ct)
    {
        return await _dbContext.WebhookSubscriptions
            .Where(s => s.MerchantId == merchantId)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task UpdateAsync(WebhookSubscription subscription, CancellationToken ct)
    {
        _dbContext.WebhookSubscriptions.Update(subscription);
        await _dbContext.SaveChangesAsync(ct);
    }
}
