namespace CardManagement.Infrastructure.Channels.Nibss;

/// <summary>
/// Publishes mandate lifecycle events to Kafka for downstream consumers.
/// </summary>
public interface IMandateEventPublisher
{
    /// <summary>
    /// Publishes an event indicating a new mandate was created.
    /// </summary>
    Task PublishMandateCreatedAsync(string mandateReference, string debtorAccount, string creditorAccount, decimal amount, CancellationToken ct);

    /// <summary>
    /// Publishes an event indicating a mandate was activated.
    /// </summary>
    Task PublishMandateActivatedAsync(string mandateReference, CancellationToken ct);

    /// <summary>
    /// Publishes an event indicating a successful debit was executed against a mandate.
    /// </summary>
    Task PublishMandateDebitedAsync(string mandateReference, string transactionReference, decimal amount, CancellationToken ct);

    /// <summary>
    /// Publishes an event indicating a mandate debit failed.
    /// </summary>
    Task PublishMandateFailedAsync(string mandateReference, string transactionReference, string errorCode, string errorMessage, CancellationToken ct);

    /// <summary>
    /// Publishes an event indicating a mandate was cancelled.
    /// </summary>
    Task PublishMandateCancelledAsync(string mandateReference, CancellationToken ct);
}
