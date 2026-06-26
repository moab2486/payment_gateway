using CardManagement.Application.DTOs;
using CardManagement.Application.PlatformServices.Webhooks.Commands;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;

namespace CardManagement.Application.PlatformServices.Webhooks.Handlers;

/// <summary>
/// Handles retry of a failed webhook delivery using the delivery engine.
/// </summary>
public class RetryDeliveryCommandHandler
{
    private readonly IWebhookDeliveryRepository _deliveryRepository;
    private readonly IWebhookDeliveryEngine _deliveryEngine;
    private readonly IAuditStore _auditStore;

    public RetryDeliveryCommandHandler(
        IWebhookDeliveryRepository deliveryRepository,
        IWebhookDeliveryEngine deliveryEngine,
        IAuditStore auditStore)
    {
        _deliveryRepository = deliveryRepository;
        _deliveryEngine = deliveryEngine;
        _auditStore = auditStore;
    }

    public async Task<Result> HandleAsync(RetryDeliveryCommand command, CancellationToken ct)
    {
        var delivery = await _deliveryRepository.GetByIdAsync(command.DeliveryId, ct);
        if (delivery is null)
        {
            return Result.Failure(
                "Delivery not found.",
                "DELIVERY_NOT_FOUND");
        }

        await _deliveryEngine.RetryAsync(command.DeliveryId, ct);

        // Audit log
        var auditEntry = AuditEntry.Create(
            transactionReference: $"webhook-delivery:{command.DeliveryId}",
            actorIdentity: "system",
            action: "WebhookDelivery.RetryRequested",
            previousState: $"Status={delivery.Status}, Attempts={delivery.AttemptCount}",
            newState: null,
            correlationId: command.DeliveryId.ToString(),
            previousEntryHash: null);

        await _auditStore.AppendAsync(auditEntry, ct);

        return Result.Success();
    }
}
