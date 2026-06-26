using CardManagement.Application.DTOs;

namespace CardManagement.Application.Ports;

/// <summary>
/// Port interface for publishing domain events to an event streaming system.
/// Implementations must be non-blocking relative to the primary transaction flow.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes a card issuance event.
    /// </summary>
    /// <param name="cardEvent">The card issued event data.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    Task PublishCardIssuedAsync(CardIssuedEvent cardEvent, CancellationToken ct);

    /// <summary>
    /// Publishes a transaction authorization event.
    /// </summary>
    /// <param name="txEvent">The transaction authorized event data.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    Task PublishTransactionAuthorizedAsync(TransactionAuthorizedEvent txEvent, CancellationToken ct);

    /// <summary>
    /// Publishes a transaction reversal event.
    /// </summary>
    /// <param name="txEvent">The transaction reversed event data.</param>
    /// <param name="ct">Cancellation token for cooperative cancellation.</param>
    Task PublishTransactionReversedAsync(TransactionReversedEvent txEvent, CancellationToken ct);
}
