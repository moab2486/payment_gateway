using CardManagement.Application.DTOs;
using CardManagement.Application.PlatformServices.Webhooks.Commands;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.Webhooks;
using System.Text.Json;

namespace CardManagement.Application.PlatformServices.Webhooks.Handlers;

/// <summary>
/// Handles delivery of a webhook event to all active subscriptions matching the event type.
/// Constructs the delivery payload with event metadata, computes HMAC signature, and dispatches.
/// </summary>
public class DeliverWebhookCommandHandler
{
    private readonly IWebhookSubscriptionRepository _subscriptionRepository;
    private readonly IWebhookDeliveryRepository _deliveryRepository;
    private readonly IWebhookDeliveryEngine _deliveryEngine;
    private readonly IHmacSigner _hmacSigner;
    private readonly IAuditStore _auditStore;

    public DeliverWebhookCommandHandler(
        IWebhookSubscriptionRepository subscriptionRepository,
        IWebhookDeliveryRepository deliveryRepository,
        IWebhookDeliveryEngine deliveryEngine,
        IHmacSigner hmacSigner,
        IAuditStore auditStore)
    {
        _subscriptionRepository = subscriptionRepository;
        _deliveryRepository = deliveryRepository;
        _deliveryEngine = deliveryEngine;
        _hmacSigner = hmacSigner;
        _auditStore = auditStore;
    }

    public async Task<Result> HandleAsync(DeliverWebhookCommand command, CancellationToken ct)
    {
        // Find all active subscriptions for this event type
        var subscriptions = await _subscriptionRepository.GetActiveByEventTypeAsync(command.EventType, ct);

        if (subscriptions.Count == 0)
        {
            return Result.Success();
        }

        foreach (var subscription in subscriptions)
        {
            // Construct the delivery payload with event metadata
            var payload = JsonSerializer.Serialize(new
            {
                event_type = command.EventType,
                data = command.EventData,
                timestamp = command.TimestampUtc.ToString("O"),
                delivery_id = Guid.NewGuid().ToString()
            });

            // Compute HMAC-SHA256 signature
            var signature = _hmacSigner.ComputeSignature(payload, subscription.SigningSecret);

            // Create delivery record
            var delivery = WebhookDelivery.Create(
                subscription.Id,
                command.EventType,
                payload);

            await _deliveryRepository.CreateAsync(delivery, ct);

            // Dispatch via the delivery engine
            await _deliveryEngine.DeliverAsync(delivery, ct);
        }

        // Audit log
        var auditEntry = AuditEntry.Create(
            transactionReference: $"webhook-delivery:{command.EventType}",
            actorIdentity: "system",
            action: "WebhookDelivery.Dispatched",
            previousState: null,
            newState: $"EventType={command.EventType}, SubscriptionCount={subscriptions.Count}",
            correlationId: Guid.NewGuid().ToString(),
            previousEntryHash: null);

        await _auditStore.AppendAsync(auditEntry, ct);

        return Result.Success();
    }
}
