using CardManagement.Application.Ports;
using CardManagement.Application.PlatformServices.Reconciliation.Commands;
using CardManagement.Application.PlatformServices.Reconciliation.DTOs;
using CardManagement.Application.PlatformServices.Reconciliation.Ports;
using CardManagement.Domain.Entities;
using CardManagement.Domain.PlatformServices.Reconciliation;

namespace CardManagement.Application.PlatformServices.Reconciliation.Handlers;

/// <summary>
/// Handles creation of manual adjustments for reconciliation exceptions.
/// Validates the adjustment amount and reason, resolves the exception,
/// persists the adjustment, and publishes an event to Kafka.
/// </summary>
public class CreateManualAdjustmentCommandHandler
{
    private readonly IReconciliationRepository _repository;
    private readonly IAuditStore _auditStore;
    private readonly IReconciliationEventPublisher _eventPublisher;

    public CreateManualAdjustmentCommandHandler(
        IReconciliationRepository repository,
        IAuditStore auditStore,
        IReconciliationEventPublisher eventPublisher)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    public async Task<CreateManualAdjustmentResult> HandleAsync(
        CreateManualAdjustmentCommand command,
        CancellationToken ct)
    {
        if (command is null)
            throw new ArgumentNullException(nameof(command));

        // Validate required fields
        if (command.ExceptionId == Guid.Empty)
            return new CreateManualAdjustmentResult(Guid.Empty, false, "Exception ID is required.");

        if (command.Amount is null)
            return new CreateManualAdjustmentResult(Guid.Empty, false, "Adjustment amount is required.");

        if (string.IsNullOrWhiteSpace(command.Reason))
            return new CreateManualAdjustmentResult(Guid.Empty, false, "Adjustment reason is required.");

        if (string.IsNullOrWhiteSpace(command.OperatorId))
            return new CreateManualAdjustmentResult(Guid.Empty, false, "Operator ID is required.");

        // Load the exception
        var exception = await _repository.GetExceptionAsync(command.ExceptionId, ct);
        if (exception is null)
            return new CreateManualAdjustmentResult(Guid.Empty, false, $"Exception '{command.ExceptionId}' not found.");

        // Create the manual adjustment
        var adjustment = Adjustment.CreateManual(
            exceptionId: command.ExceptionId,
            amount: command.Amount,
            reason: command.Reason,
            operatorId: command.OperatorId);

        // Resolve the exception manually
        exception.ResolveManually(adjustment.Id);

        // Persist
        await _repository.AddAdjustmentAsync(adjustment, ct);
        await _repository.UpdateExceptionAsync(exception, ct);

        // Audit trail
        var auditEntry = AuditEntry.Create(
            transactionReference: $"adjustment:{adjustment.Id}",
            actorIdentity: command.OperatorId,
            action: "reconciliation.manual_adjustment_created",
            previousState: null,
            newState: $"ExceptionId={command.ExceptionId};Amount={command.Amount};Reason={command.Reason}",
            correlationId: command.ExceptionId.ToString(),
            previousEntryHash: null);

        await _auditStore.AppendAsync(auditEntry, ct);

        // Publish event for downstream consumption (ledger, reporting)
        await _eventPublisher.PublishAdjustmentCreatedAsync(
            new AdjustmentCreatedEvent(
                AdjustmentId: adjustment.Id,
                ExceptionId: command.ExceptionId,
                Amount: command.Amount.Amount,
                CurrencyCode: command.Amount.CurrencyCode,
                Reason: command.Reason,
                OperatorId: command.OperatorId,
                Type: AdjustmentType.ManualOperator,
                CreatedAtUtc: DateTime.UtcNow),
            ct);

        return new CreateManualAdjustmentResult(adjustment.Id, true);
    }
}
