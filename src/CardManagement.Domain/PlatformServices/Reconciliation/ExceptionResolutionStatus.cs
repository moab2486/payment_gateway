namespace CardManagement.Domain.PlatformServices.Reconciliation;

/// <summary>
/// Tracks the resolution state of a reconciliation exception.
/// </summary>
public enum ExceptionResolutionStatus
{
    Pending,
    AutoResolved,
    ManualResolved,
    Expired
}
