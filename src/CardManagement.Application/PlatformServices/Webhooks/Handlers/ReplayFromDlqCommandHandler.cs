using CardManagement.Application.DTOs;
using CardManagement.Application.PlatformServices.Webhooks.Commands;
using CardManagement.Application.PlatformServices.Webhooks.Ports;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;

namespace CardManagement.Application.PlatformServices.Webhooks.Handlers;

/// <summary>
/// Handles replay of a dead-lettered webhook delivery. Re-attempts delivery
/// of the original payload with a fresh HMAC signature.
/// </summary>
public class ReplayFromDlqCommandHandler
{
    private readonly IWebhookDeliveryRepository _deliveryRepository;
    private readonly IWebhookDeliveryEngine _deliveryEngine;
    private readonly IAuditStore _auditStore;

    public ReplayFromDlqCommandHandler(
        IWebhookDeliveryRepository deliveryRepository,
        IWebhookDeliveryEngine deliveryEngine,
        IAuditStore auditStore)
    {
        _deliveryRepository = deliveryRepository;
        _deliveryEngine = deliveryEngine;
        _auditStore = auditStore;
    }

    public async Task<Result> HandleAsync(ReplayFromDlqCommand command, CancellationToken ct)
    {
        var dlqItem = await _deliveryRepository.GetDlqItemByIdAsync(command.DlqItemId, ct);
        if (dlqItem is null)
        {
            return Result.Failure(
                "DLQ item not found.",
                "DLQ_ITEM_NOT_FOUND");
        }

        if (dlqItem.Replayed)
        {
            return Result.Failure(
                "This DLQ item has already been replayed.",
                "DLQ_ITEM_ALREADY_REPLAYED");
        }

        await _deliveryEngine.ReplayFromDlqAsync(command.DlqItemId, ct);

        // Audit log
        var auditEntry = AuditEntry.Create(
            transactionReference: $"webhook-dlq:{command.DlqItemId}",
            actorIdentity: "system",
            action: "WebhookDlq.ReplayRequested",
            previousState: $"DeliveryId={dlqItem.DeliveryId}, LastError={dlqItem.LastError}",
            newState: "Replaying",
            correlationId: command.DlqItemId.ToString(),
            previousEntryHash: null);

        await _auditStore.AppendAsync(auditEntry, ct);

        return Result.Success();
    }
}
