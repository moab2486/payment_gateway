using CardManagement.Application.DTOs;
using CardManagement.Application.Ports;
using CardManagement.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CardManagement.Infrastructure.Saga;

/// <summary>
/// Saga orchestrator that executes multi-step distributed transactions with durable state persistence.
/// On failure, compensates all previously completed steps in reverse order with exponential backoff retry.
/// Publishes saga lifecycle events to Kafka on completion, rollback, or manual intervention.
/// </summary>
public sealed class SagaOrchestrator : ISagaOrchestrator
{
    private readonly ISagaStateRepository _repository;
    private readonly ISagaEventPublisher _eventPublisher;
    private readonly IDelayProvider _delayProvider;
    private readonly SagaOptions _options;
    private readonly ILogger<SagaOrchestrator> _logger;

    public SagaOrchestrator(
        ISagaStateRepository repository,
        ISagaEventPublisher eventPublisher,
        IDelayProvider delayProvider,
        IOptions<SagaOptions> options,
        ILogger<SagaOrchestrator> logger)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
        _delayProvider = delayProvider;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<SagaResult> ExecuteAsync(
        string transactionReference,
        IReadOnlyList<SagaStepDefinition> steps,
        CancellationToken ct)
    {
        // Create and persist initial saga state
        var stepNames = steps.Select(s => s.StepName).ToList();
        var sagaState = SagaState.Create(transactionReference, stepNames);
        await _repository.CreateAsync(sagaState, ct);

        _logger.LogInformation("Saga {TransactionReference} started with {StepCount} steps.",
            transactionReference, steps.Count);

        // Execute steps sequentially
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i];

            try
            {
                await step.ExecuteAction(ct);
                sagaState.AdvanceStep();
                await _repository.UpdateAsync(sagaState, ct);

                _logger.LogDebug("Saga {TransactionReference} step {StepName} completed successfully.",
                    transactionReference, step.StepName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Saga {TransactionReference} step {StepName} failed: {Error}",
                    transactionReference, step.StepName, ex.Message);

                sagaState.FailCurrentStep(ex.Message);
                await _repository.UpdateAsync(sagaState, ct);

                // Run compensation for all previously completed steps in reverse order
                var compensationSuccess = await RunCompensationsAsync(
                    sagaState, steps, i - 1, transactionReference, ct);

                if (compensationSuccess)
                {
                    sagaState.MarkRolledBack();
                    await _repository.UpdateAsync(sagaState, ct);

                    await _eventPublisher.PublishSagaRolledBackAsync(
                        transactionReference, step.StepName, ex.Message, ct);

                    _logger.LogInformation("Saga {TransactionReference} rolled back successfully after failure at step {StepName}.",
                        transactionReference, step.StepName);
                }
                else
                {
                    sagaState.MarkFailedManualIntervention();
                    await _repository.UpdateAsync(sagaState, ct);

                    await _eventPublisher.PublishSagaManualInterventionRequiredAsync(
                        transactionReference, step.StepName, _options.MaxCompensationRetries, ct);

                    _logger.LogError("Saga {TransactionReference} compensation failed. Flagged for manual intervention.",
                        transactionReference);
                }

                return new SagaResult(transactionReference, false, step.StepName, ex.Message);
            }
        }

        // All steps completed successfully
        await _eventPublisher.PublishSagaCompletedAsync(transactionReference, ct);

        _logger.LogInformation("Saga {TransactionReference} completed successfully.", transactionReference);

        return new SagaResult(transactionReference, true, null, null);
    }

    /// <summary>
    /// Runs compensations in reverse order from the specified last completed step index (inclusive).
    /// Returns true if all compensations succeeded, false if any exhausted retries.
    /// </summary>
    private async Task<bool> RunCompensationsAsync(
        SagaState sagaState,
        IReadOnlyList<SagaStepDefinition> steps,
        int lastCompletedStepIndex,
        string transactionReference,
        CancellationToken ct)
    {
        for (var i = lastCompletedStepIndex; i >= 0; i--)
        {
            var step = steps[i];
            var success = await RetryCompensationAsync(step, transactionReference, ct);

            if (success)
            {
                sagaState.MarkStepCompensated(i);
                await _repository.UpdateAsync(sagaState, ct);
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Retries a compensation action with exponential backoff.
    /// Returns true if compensation succeeded within max retries, false otherwise.
    /// </summary>
    private async Task<bool> RetryCompensationAsync(
        SagaStepDefinition step,
        string transactionReference,
        CancellationToken ct)
    {
        var delayMs = _options.InitialRetryDelayMs;

        for (var attempt = 0; attempt <= _options.MaxCompensationRetries; attempt++)
        {
            try
            {
                await step.CompensateAction(ct);

                _logger.LogDebug("Saga {TransactionReference} compensation for step {StepName} succeeded on attempt {Attempt}.",
                    transactionReference, step.StepName, attempt + 1);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Saga {TransactionReference} compensation for step {StepName} failed on attempt {Attempt}/{MaxRetries}.",
                    transactionReference, step.StepName, attempt + 1, _options.MaxCompensationRetries + 1);

                if (attempt < _options.MaxCompensationRetries)
                {
                    await _delayProvider.DelayAsync(TimeSpan.FromMilliseconds(delayMs), ct);
                    delayMs *= 2; // Exponential backoff: 1s, 2s, 4s, 8s...
                }
            }
        }

        _logger.LogError(
            "Saga {TransactionReference} compensation for step {StepName} exhausted all {MaxRetries} retries.",
            transactionReference, step.StepName, _options.MaxCompensationRetries + 1);

        return false;
    }
}
