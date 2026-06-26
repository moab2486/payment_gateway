using CardManagement.Application.DTOs;

namespace CardManagement.Application.Ports;

/// <summary>
/// Executes multi-step sagas with durable state persistence and automatic compensation on failure.
/// </summary>
public interface ISagaOrchestrator
{
    Task<SagaResult> ExecuteAsync(string transactionReference, IReadOnlyList<SagaStepDefinition> steps, CancellationToken ct);
}
