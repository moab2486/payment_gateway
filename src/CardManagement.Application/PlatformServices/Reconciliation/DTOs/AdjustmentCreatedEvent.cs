using CardManagement.Domain.PlatformServices.Reconciliation;

namespace CardManagement.Application.PlatformServices.Reconciliation.DTOs;

/// <summary>
/// Event published when a reconciliation adjustment is created (automatic or manual).
/// Published to Kafka for downstream consumption by ledger and reporting systems.
/// </summary>
public record AdjustmentCreatedEvent(
    Guid AdjustmentId,
    Guid ExceptionId,
    long Amount,
    string CurrencyCode,
    string Reason,
    string OperatorId,
    AdjustmentType Type,
    DateTime CreatedAtUtc);
