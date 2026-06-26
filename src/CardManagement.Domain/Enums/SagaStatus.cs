namespace CardManagement.Domain.Enums;

public enum SagaStatus
{
    Running,
    Completed,
    Compensating,
    RolledBack,
    FailedManualIntervention
}
