using CardManagement.Application.DTOs;
using CardManagement.Application.PlatformServices.Webhooks.Commands;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.Webhooks;

namespace CardManagement.Application.PlatformServices.Webhooks.Handlers;

/// <summary>
/// Handles deactivation of a webhook subscription. Ceases all deliveries for the
/// subscription while retaining delivery history.
/// </summary>
public class DeactivateSubscriptionCommandHandler
{
    private readonly IWebhookSubscriptionRepository _subscriptionRepository;
    private readonly IAuditStore _auditStore;

    public DeactivateSubscriptionCommandHandler(
        IWebhookSubscriptionRepository subscriptionRepository,
        IAuditStore auditStore)
    {
        _subscriptionRepository = subscriptionRepository;
        _auditStore = auditStore;
    }

    public async Task<Result> HandleAsync(DeactivateSubscriptionCommand command, CancellationToken ct)
    {
        var subscription = await _subscriptionRepository.GetByIdAsync(command.SubscriptionId, ct);
        if (subscription is null)
        {
            return Result.Failure(
                "Subscription not found.",
                "SUBSCRIPTION_NOT_FOUND");
        }

        var previousStatus = subscription.Status.ToString();

        subscription.Deactivate();

        await _subscriptionRepository.UpdateAsync(subscription, ct);

        // Audit log
        var auditEntry = AuditEntry.Create(
            transactionReference: $"webhook-subscription:{subscription.Id}",
            actorIdentity: subscription.MerchantId.ToString(),
            action: "WebhookSubscription.Deactivated",
            previousState: $"Status={previousStatus}",
            newState: $"Status={subscription.Status}",
            correlationId: subscription.Id.ToString(),
            previousEntryHash: null);

        await _auditStore.AppendAsync(auditEntry, ct);

        return Result.Success();
    }
}
