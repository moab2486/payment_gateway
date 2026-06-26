namespace CardManagement.Domain.PlatformServices.Reconciliation;

/// <summary>
/// Indicates whether an adjustment was created automatically by a rule or manually by an operator.
/// </summary>
public enum AdjustmentType
{
    AutoRule,
    ManualOperator
}
