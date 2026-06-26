using CardManagement.Domain.Enums;

namespace CardManagement.Domain.ValueObjects;

/// <summary>
/// Represents a single step within a saga, tracking its execution and compensation state.
/// </summary>
public record SagaStep
{
    public string StepName { get; }
    public SagaStepStatus Status { get; init; }
    public DateTime? ExecutedAtUtc { get; init; }
    public DateTime? CompensatedAtUtc { get; init; }
    public int RetryCount { get; init; }
    public string? ErrorMessage { get; init; }

    public SagaStep(string stepName)
    {
        if (string.IsNullOrWhiteSpace(stepName))
            throw new ArgumentException("Step name is required.", nameof(stepName));

        StepName = stepName;
        Status = SagaStepStatus.Pending;
        RetryCount = 0;
    }
}
