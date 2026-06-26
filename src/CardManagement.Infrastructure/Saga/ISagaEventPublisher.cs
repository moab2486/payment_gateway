namespace CardManagement.Infrastructure.Saga;

/// <summary>
/// Publishes saga lifecycle events to Kafka.
/// </summary>
public interface ISagaEventPublisher
{
    /// <summary>
    /// Publishes a saga-completed event when all steps finish successfully.
    /// </summary>
    Task PublishSagaCompletedAsync(string transactionReference, CancellationToken ct);

    /// <summary>
    /// Publishes a saga-rolled-back event when all compensations complete after a failure.
    /// </summary>
    Task PublishSagaRolledBackAsync(string transactionReference, string failedStep, string errorMessage, CancellationToken ct);

    /// <summary>
    /// Publishes a saga-manual-intervention-required alert event when compensation retries are exhausted.
    /// </summary>
    Task PublishSagaManualInterventionRequiredAsync(string transactionReference, string failedStep, int retriesExhausted, CancellationToken ct);
}
