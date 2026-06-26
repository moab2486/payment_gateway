using CardManagement.Domain.ValueObjects;

namespace CardManagement.Application.PlatformServices.Reconciliation.Commands;

/// <summary>
/// Command to create a manual adjustment for a reconciliation exception.
/// </summary>
public record CreateManualAdjustmentCommand(
    Guid ExceptionId,
    Money Amount,
    string Reason,
    string OperatorId);

/// <summary>
/// Result of the create manual adjustment command.
/// </summary>
public record CreateManualAdjustmentResult(
    Guid AdjustmentId,
    bool Success,
    string? ErrorMessage = null);
