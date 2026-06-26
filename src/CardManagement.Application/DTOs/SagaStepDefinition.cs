namespace CardManagement.Application.DTOs;

/// <summary>
/// Defines a single step within a saga, including its execute and compensate actions.
/// </summary>
public record SagaStepDefinition(
    string StepName,
    Func<CancellationToken, Task> ExecuteAction,
    Func<CancellationToken, Task> CompensateAction);
