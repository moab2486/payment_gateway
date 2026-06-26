namespace CardManagement.Infrastructure.Disputes;

/// <summary>
/// Abstraction for publishing dispute lifecycle events to Kafka.
/// </summary>
public interface IDisputeEventPublisher
{
    Task PublishDisputeOpenedAsync(DisputeOpenedEvent evt, CancellationToken ct);
    Task PublishDisputeEscalatedAsync(DisputeEscalatedEvent evt, CancellationToken ct);
    Task PublishDisputeResolvedAsync(DisputeResolvedEvent evt, CancellationToken ct);
    Task PublishDisputeClosedAsync(DisputeClosedEvent evt, CancellationToken ct);
}

/// <summary>
/// Event published when a dispute is opened.
/// </summary>
public record DisputeOpenedEvent(
    Guid DisputeId,
    string TransactionReference,
    string ReasonCode,
    decimal Amount,
    string CurrencyCode);

/// <summary>
/// Event published when a dispute is escalated.
/// </summary>
public record DisputeEscalatedEvent(
    Guid DisputeId,
    string TransactionReference);

/// <summary>
/// Event published when a dispute is resolved.
/// </summary>
public record DisputeResolvedEvent(
    Guid DisputeId,
    string TransactionReference,
    string Resolution);

/// <summary>
/// Event published when a dispute is closed.
/// </summary>
public record DisputeClosedEvent(
    Guid DisputeId,
    string TransactionReference);
