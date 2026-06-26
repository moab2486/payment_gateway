using CardManagement.Domain.Enums;
using CardManagement.Domain.ValueObjects;

namespace CardManagement.Domain.Entities;

/// <summary>
/// Represents the persistent state of a saga orchestrating a distributed transaction.
/// Tracks step progression and supports compensation on failure.
/// </summary>
public class SagaState
{
    public Guid Id { get; private set; }
    public string TransactionReference { get; private set; } = string.Empty;
    public int CurrentStepIndex { get; private set; }
    public List<SagaStep> Steps { get; private set; } = new();
    public SagaStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    private SagaState() { }

    public static SagaState Create(string transactionReference, IReadOnlyList<string> stepNames)
    {
        if (string.IsNullOrWhiteSpace(transactionReference))
            throw new ArgumentException("Transaction reference is required.", nameof(transactionReference));

        if (stepNames is null || stepNames.Count == 0)
            throw new ArgumentException("At least one step is required.", nameof(stepNames));

        var now = TruncateToMilliseconds(DateTime.UtcNow);

        return new SagaState
        {
            Id = Guid.NewGuid(),
            TransactionReference = transactionReference,
            CurrentStepIndex = 0,
            Steps = stepNames.Select(name => new SagaStep(name)).ToList(),
            Status = SagaStatus.Running,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
    }

    public void AdvanceStep()
    {
        if (Status != SagaStatus.Running)
            throw new InvalidOperationException("Can only advance steps in a running saga.");

        if (CurrentStepIndex >= Steps.Count)
            throw new InvalidOperationException("No more steps to advance.");

        var now = TruncateToMilliseconds(DateTime.UtcNow);

        Steps[CurrentStepIndex] = Steps[CurrentStepIndex] with
        {
            Status = SagaStepStatus.Completed,
            ExecutedAtUtc = now
        };

        CurrentStepIndex++;
        UpdatedAtUtc = now;

        if (CurrentStepIndex >= Steps.Count)
        {
            Status = SagaStatus.Completed;
        }
    }

    public void FailCurrentStep(string errorMessage)
    {
        if (Status != SagaStatus.Running)
            throw new InvalidOperationException("Can only fail steps in a running saga.");

        var now = TruncateToMilliseconds(DateTime.UtcNow);

        Steps[CurrentStepIndex] = Steps[CurrentStepIndex] with
        {
            Status = SagaStepStatus.Failed,
            ErrorMessage = errorMessage,
            ExecutedAtUtc = now
        };

        Status = SagaStatus.Compensating;
        UpdatedAtUtc = now;
    }

    public void MarkStepCompensated(int stepIndex)
    {
        if (Status != SagaStatus.Compensating)
            throw new InvalidOperationException("Can only compensate steps in a compensating saga.");

        if (stepIndex < 0 || stepIndex >= Steps.Count)
            throw new ArgumentOutOfRangeException(nameof(stepIndex));

        var now = TruncateToMilliseconds(DateTime.UtcNow);

        Steps[stepIndex] = Steps[stepIndex] with
        {
            Status = SagaStepStatus.Compensated,
            CompensatedAtUtc = now
        };

        UpdatedAtUtc = now;
    }

    public void MarkRolledBack()
    {
        if (Status != SagaStatus.Compensating)
            throw new InvalidOperationException("Can only roll back a compensating saga.");

        Status = SagaStatus.RolledBack;
        UpdatedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    public void MarkFailedManualIntervention()
    {
        if (Status != SagaStatus.Compensating)
            throw new InvalidOperationException("Can only flag for manual intervention from compensating state.");

        Status = SagaStatus.FailedManualIntervention;
        UpdatedAtUtc = TruncateToMilliseconds(DateTime.UtcNow);
    }

    private static DateTime TruncateToMilliseconds(DateTime dateTime)
    {
        return new DateTime(
            dateTime.Ticks - (dateTime.Ticks % TimeSpan.TicksPerMillisecond),
            DateTimeKind.Utc);
    }
}
