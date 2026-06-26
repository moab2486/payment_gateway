using CardManagement.Application.DTOs;
using CardManagement.Application.PlatformServices.Webhooks.Commands;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.Webhooks;

namespace CardManagement.Application.PlatformServices.Webhooks.Handlers;

/// <summary>
/// Handles creation of webhook subscriptions including URL verification challenge
/// and subscription limit enforcement (max 50 per merchant).
/// </summary>
public class CreateSubscriptionCommandHandler
{
    private const int MaxSubscriptionsPerMerchant = 50;

    private readonly IWebhookSubscriptionRepository _subscriptionRepository;
    private readonly IUrlVerificationService _urlVerificationService;
    private readonly IAuditStore _auditStore;

    public CreateSubscriptionCommandHandler(
        IWebhookSubscriptionRepository subscriptionRepository,
        IUrlVerificationService urlVerificationService,
        IAuditStore auditStore)
    {
        _subscriptionRepository = subscriptionRepository;
        _urlVerificationService = urlVerificationService;
        _auditStore = auditStore;
    }

    public async Task<Result<WebhookSubscription>> HandleAsync(CreateSubscriptionCommand command, CancellationToken ct)
    {
        // Enforce subscription limit per merchant
        var activeCount = await _subscriptionRepository.CountActiveByMerchantAsync(command.MerchantId, ct);
        if (activeCount >= MaxSubscriptionsPerMerchant)
        {
            return Result<WebhookSubscription>.Failure(
                $"Merchant has reached the maximum of {MaxSubscriptionsPerMerchant} active subscriptions.",
                "SUBSCRIPTION_LIMIT_EXCEEDED");
        }

        // Verify destination URL via challenge-response
        var urlVerified = await _urlVerificationService.VerifyChallengeAsync(command.DestinationUrl, ct);
        if (!urlVerified)
        {
            return Result<WebhookSubscription>.Failure(
                "Destination URL failed verification challenge. The URL must respond to GET with the challenge token.",
                "URL_VERIFICATION_FAILED");
        }

        // Create the subscription
        var subscription = WebhookSubscription.Create(
            command.MerchantId,
            command.DestinationUrl,
            command.EventTypes,
            command.SigningSecret);

        var created = await _subscriptionRepository.CreateAsync(subscription, ct);

        // Audit log
        var auditEntry = AuditEntry.Create(
            transactionReference: $"webhook-subscription:{created.Id}",
            actorIdentity: command.MerchantId.ToString(),
            action: "WebhookSubscription.Created",
            previousState: null,
            newState: $"URL={command.DestinationUrl}, Events=[{string.Join(",", command.EventTypes)}]",
            correlationId: created.Id.ToString(),
            previousEntryHash: null);

        await _auditStore.AppendAsync(auditEntry, ct);

        return Result<WebhookSubscription>.Success(created);
    }
}
