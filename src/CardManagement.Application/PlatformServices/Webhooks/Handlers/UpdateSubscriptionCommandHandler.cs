using CardManagement.Application.DTOs;
using CardManagement.Application.PlatformServices.Webhooks.Commands;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.Webhooks;

namespace CardManagement.Application.PlatformServices.Webhooks.Handlers;

/// <summary>
/// Handles updates to an existing webhook subscription's destination URL and/or event types.
/// Changes apply to subsequent deliveries without affecting in-flight deliveries.
/// </summary>
public class UpdateSubscriptionCommandHandler
{
    private readonly IWebhookSubscriptionRepository _subscriptionRepository;
    private readonly IAuditStore _auditStore;

    public UpdateSubscriptionCommandHandler(
        IWebhookSubscriptionRepository subscriptionRepository,
        IAuditStore auditStore)
    {
        _subscriptionRepository = subscriptionRepository;
        _auditStore = auditStore;
    }

    public async Task<Result<WebhookSubscription>> HandleAsync(UpdateSubscriptionCommand command, CancellationToken ct)
    {
        var subscription = await _subscriptionRepository.GetByIdAsync(command.SubscriptionId, ct);
        if (subscription is null)
        {
            return Result<WebhookSubscription>.Failure(
                "Subscription not found.",
                "SUBSCRIPTION_NOT_FOUND");
        }

        var previousState = $"URL={subscription.DestinationUrl}, Events=[{string.Join(",", subscription.EventTypes)}]";

        subscription.Update(command.DestinationUrl, command.EventTypes);

        await _subscriptionRepository.UpdateAsync(subscription, ct);

        var newState = $"URL={subscription.DestinationUrl}, Events=[{string.Join(",", subscription.EventTypes)}]";

        // Audit log
        var auditEntry = AuditEntry.Create(
            transactionReference: $"webhook-subscription:{subscription.Id}",
            actorIdentity: subscription.MerchantId.ToString(),
            action: "WebhookSubscription.Updated",
            previousState: previousState,
            newState: newState,
            correlationId: subscription.Id.ToString(),
            previousEntryHash: null);

        await _auditStore.AppendAsync(auditEntry, ct);

        return Result<WebhookSubscription>.Success(subscription);
    }
}
