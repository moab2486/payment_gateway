using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Domain.PlatformServices.Webhooks;
using CardManagement.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CardManagement.Infrastructure.PlatformServices.Webhooks.Persistence;

/// <summary>
/// EF Core implementation of IWebhookDeliveryRepository.
/// Provides CRUD operations for deliveries, DLQ move functionality, and DLQ queries.
/// </summary>
public class WebhookDeliveryRepository : IWebhookDeliveryRepository
{
    private readonly CardManagementDbContext _dbContext;

    public WebhookDeliveryRepository(CardManagementDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    public async Task<WebhookDelivery> CreateAsync(WebhookDelivery delivery, CancellationToken ct)
    {
        _dbContext.WebhookDeliveries.Add(delivery);
        await _dbContext.SaveChangesAsync(ct);
        return delivery;
    }

    public async Task UpdateAsync(WebhookDelivery delivery, CancellationToken ct)
    {
        _dbContext.WebhookDeliveries.Update(delivery);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<WebhookDelivery?> GetByIdAsync(Guid deliveryId, CancellationToken ct)
    {
        return await _dbContext.WebhookDeliveries.FindAsync(new object[] { deliveryId }, ct);
    }

    public async Task<IReadOnlyList<WebhookDelivery>> GetBySubscriptionAsync(
        Guid subscriptionId, int limit, int offset, CancellationToken ct)
    {
        return await _dbContext.WebhookDeliveries
            .Where(d => d.SubscriptionId == subscriptionId)
            .OrderByDescending(d => d.CreatedAtUtc)
            .Skip(offset)
            .Take(limit)
            .ToListAsync(ct);
    }

    public async Task MoveToDlqAsync(Guid deliveryId, CancellationToken ct)
    {
        var delivery = await _dbContext.WebhookDeliveries.FindAsync(new object[] { deliveryId }, ct);
        if (delivery is null)
            throw new InvalidOperationException($"Delivery with ID {deliveryId} not found.");

        // Mark the delivery as dead-lettered
        delivery.MoveToDlq();

        // Get the last error from the most recent attempt
        var lastError = delivery.Attempts.Count > 0
            ? delivery.Attempts[^1].ErrorMessage ?? $"HTTP {delivery.Attempts[^1].HttpStatusCode}"
            : "Unknown error";

        // Create DLQ item
        var dlqItem = DlqItem.Create(
            deliveryId: delivery.Id,
            subscriptionId: delivery.SubscriptionId,
            originalPayload: delivery.Payload,
            lastError: lastError);

        _dbContext.WebhookDlqItems.Add(dlqItem);
        await _dbContext.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<DlqItem>> GetDlqItemsAsync(Guid subscriptionId, CancellationToken ct)
    {
        return await _dbContext.WebhookDlqItems
            .Where(d => d.SubscriptionId == subscriptionId)
            .OrderByDescending(d => d.MovedToDlqAtUtc)
            .ToListAsync(ct);
    }

    public async Task<DlqItem?> GetDlqItemByIdAsync(Guid dlqItemId, CancellationToken ct)
    {
        return await _dbContext.WebhookDlqItems.FindAsync(new object[] { dlqItemId }, ct);
    }
}
